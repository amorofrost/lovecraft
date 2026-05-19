using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Entities;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Services;

public class EventReminderProcessor : IEventReminderProcessor
{
    // Worker-side channels: Telegram + Email are enqueued to the outbox. InApp + WebPush
    // are in-process channels owned by the API (SignalR / VAPID) — the worker has no
    // SignalR hub or VAPID keys, so we deliberately skip them here. Recipients still get
    // the canonical row, which lights up the bell on next poll.
    private static readonly string[] WorkerChannels = { "Telegram", "Email" };

    private readonly TableClient _events;
    private readonly TableClient _attendees;
    private readonly TableClient _notifications;
    private readonly TableClient _outbox;
    private readonly TableClient _preferences;
    private readonly ILogger<EventReminderProcessor> _logger;

    public EventReminderProcessor(
        TableClient events,
        TableClient attendees,
        TableClient notifications,
        TableClient outbox,
        TableClient preferences,
        ILogger<EventReminderProcessor> logger)
    {
        _events = events;
        _attendees = attendees;
        _notifications = notifications;
        _outbox = outbox;
        _preferences = preferences;
        _logger = logger;
    }

    public async Task RunAsync(DateTime now, CancellationToken ct)
    {
        // NOTE: This method duplicates a subset of `Lovecraft.Backend.Services.Notifications.NotificationProducer.ProduceAsync`.
        // The duplication is intentional: NotificationsWorker is isolated from Backend (no cross-project reference).
        // Drift risk is mitigated by integration tests against the shared Azure Table schema.

        var windowStart = now.AddHours(23);
        var windowEnd = now.AddHours(25);

        // Scan the events partition. We filter client-side rather than via OData `Date ge ...`
        // because Azure Table Storage's DateTime filter syntax is brittle; the partition is
        // small (relative to user counts) and a single round-trip is cheap. Archived rows are
        // also filtered client-side.
        var eventsFilter = "PartitionKey eq 'EVENTS'";
        await foreach (var evt in _events.QueryAsync<EventEntity>(eventsFilter).WithCancellation(ct))
        {
            if (evt.Archived) continue;
            if (evt.Date < windowStart || evt.Date >= windowEnd) continue;

            var eventId = evt.RowKey;
            var sourceEventId = $"event-reminder-{eventId}";

            // The payload mirrors what the EventPublished producer writes in
            // AzureEventService so renderers can format the EventReminder uniformly.
            var payloadJson = JsonSerializer.Serialize(new
            {
                eventId,
                eventTitle = evt.Title,
                eventDateUtc = evt.Date.ToString("o"),
            });

            // Iterate attendees (PK = eventId, RK = userId)
            var attendeeFilter = $"PartitionKey eq '{eventId.Replace("'", "''")}'";
            await foreach (var attendee in _attendees.QueryAsync<EventAttendeeEntity>(attendeeFilter).WithCancellation(ct))
            {
                var recipientId = attendee.RowKey;
                if (string.IsNullOrEmpty(recipientId)) continue;

                try
                {
                    await ProduceForRecipientAsync(recipientId, eventId, sourceEventId, payloadJson, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "EventReminder produce failed for {RecipientId} on event {EventId}",
                        recipientId, eventId);
                }
            }
        }
    }

    private async Task ProduceForRecipientAsync(
        string recipientId, string eventId, string sourceEventId, string payloadJson, CancellationToken ct)
    {
        // Idempotency: if any notification row in the recipient's partition already references
        // this sourceEventId, skip. This makes overlapping 5-minute scans within the 2-hour
        // reminder window safe.
        if (await AlreadyRemindedAsync(recipientId, sourceEventId, ct))
            return;

        var now = DateTime.UtcNow;
        var notificationId = Guid.NewGuid().ToString("N");

        var canonical = new NotificationEntity
        {
            PartitionKey = NotificationEntity.GetPartitionKey(recipientId),
            RowKey = NotificationEntity.GetRowKey(notificationId, now),
            NotificationId = notificationId,
            UserId = recipientId,
            Type = "EventReminder",
            ActorId = null,
            PayloadJson = payloadJson,
            CreatedAtUtc = now,
            SourceEventId = sourceEventId,
            IsRead = false,
            IsDismissed = false,
        };
        await _notifications.AddEntityAsync(canonical, ct);

        // Read preferences to decide which worker-side channels to enqueue.
        // Mute / snooze short-circuits all outbox enqueues — canonical row is still written
        // (the bell IS the inbox; mute only suppresses out-of-app channels).
        NotificationPreferencesEntity? prefs = null;
        try
        {
            var resp = await _preferences.GetEntityAsync<NotificationPreferencesEntity>(recipientId, "INDEX", cancellationToken: ct);
            prefs = resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No prefs row → defaults: only inApp (in-process, not enqueued here). No outbox writes.
        }

        if (prefs is null) return;
        if (prefs.Mute) return;
        if (prefs.MutedUntilUtc.HasValue && prefs.MutedUntilUtc.Value > DateTime.UtcNow) return;

        // Resolve matrix["eventReminder"][<channelKey>] from MatrixJson.
        var (matrixRow, frequencyMap, dailyHourUtc) = ParsePrefs(prefs);

        foreach (var channel in WorkerChannels)
        {
            var channelKey = char.ToLowerInvariant(channel[0]) + channel[1..];
            if (!matrixRow.TryGetValue(channelKey, out var enabled) || !enabled) continue;

            // Frequency map keys match the channel key (e.g. "telegram", "email").
            var frequency = frequencyMap.TryGetValue(channelKey, out var f) ? f : "Immediate";
            var scheduledFor = ScheduleFor(now, frequency, dailyHourUtc);

            var outboxRow = new NotificationOutboxEntity
            {
                PartitionKey = NotificationOutboxEntity.PendingPartition(channel),
                RowKey = NotificationOutboxEntity.GetRowKey(scheduledFor, notificationId),
                UserId = recipientId,
                NotificationId = notificationId,
                Channel = channel,
                Frequency = frequency,
                ScheduledForUtc = scheduledFor,
            };
            try
            {
                await _outbox.AddEntityAsync(outboxRow, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enqueue EventReminder outbox row for {Channel}/{NotificationId}",
                    channel, notificationId);
            }
        }
    }

    private async Task<bool> AlreadyRemindedAsync(string recipientId, string sourceEventId, CancellationToken ct)
    {
        var filter = $"PartitionKey eq '{recipientId.Replace("'", "''")}' and SourceEventId eq '{sourceEventId.Replace("'", "''")}'";
        await foreach (var _ in _notifications.QueryAsync<NotificationEntity>(filter, maxPerPage: 1).WithCancellation(ct))
        {
            return true;
        }
        return false;
    }

    private static (Dictionary<string, bool> MatrixRow, Dictionary<string, string> FrequencyMap, int DailyHourUtc)
        ParsePrefs(NotificationPreferencesEntity prefs)
    {
        var matrixRow = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(prefs.MatrixJson ?? "{}");
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("eventReminder", out var row) &&
                row.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in row.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.True) matrixRow[prop.Name] = true;
                    else if (prop.Value.ValueKind == JsonValueKind.False) matrixRow[prop.Name] = false;
                }
            }
        }
        catch (JsonException) { /* malformed → treat as empty */ }

        var frequencyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(prefs.FrequencyJson ?? "{}");
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        frequencyMap[prop.Name] = prop.Value.GetString() ?? "Immediate";
                }
            }
        }
        catch (JsonException) { /* malformed → defaults */ }

        return (matrixRow, frequencyMap, prefs.DailyDigestHourUtc);
    }

    private static DateTime ScheduleFor(DateTime now, string frequency, int dailyHourUtc) => frequency switch
    {
        "Hourly" => new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(1),
        "Daily" => NextDailySlot(now, dailyHourUtc),
        _ => now,
    };

    private static DateTime NextDailySlot(DateTime now, int hourUtc)
    {
        var today = new DateTime(now.Year, now.Month, now.Day, hourUtc, 0, 0, DateTimeKind.Utc);
        return today > now ? today : today.AddDays(1);
    }
}
