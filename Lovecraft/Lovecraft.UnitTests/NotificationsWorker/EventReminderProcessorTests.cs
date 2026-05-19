using Azure;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Entities;
using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class EventReminderProcessorTests
{
    private static EventEntity MakeEvent(string id, DateTime date, string title = "Test Event", bool archived = false) => new()
    {
        PartitionKey = "EVENTS",
        RowKey = id,
        Title = title,
        Date = date,
        Visibility = "Public",
        Archived = archived,
        ETag = new ETag("e"),
    };

    private static EventAttendeeEntity MakeAttendee(string eventId, string userId) => new()
    {
        PartitionKey = eventId,
        RowKey = userId,
        RegisteredAt = DateTime.UtcNow,
        ETag = new ETag("e"),
    };

    private static NotificationPreferencesEntity MakePrefs(
        string userId,
        bool telegram = false,
        bool email = false,
        bool mute = false,
        DateTime? mutedUntilUtc = null,
        int dailyHourUtc = 9)
    {
        var matrixJson = "{\"eventReminder\":{" +
                         $"\"inApp\":true,\"telegram\":{(telegram ? "true" : "false")},\"email\":{(email ? "true" : "false")}" +
                         "}}";
        return new NotificationPreferencesEntity
        {
            PartitionKey = userId,
            RowKey = "INDEX",
            MatrixJson = matrixJson,
            FrequencyJson = "{}",
            DailyDigestHourUtc = dailyHourUtc,
            Mute = mute,
            MutedUntilUtc = mutedUntilUtc,
            ETag = new ETag("e"),
        };
    }

    /// <summary>
    /// Captures every entity AddEntityAsync write across the canonical + outbox table mocks.
    /// </summary>
    private class Captured
    {
        public List<NotificationEntity> Notifications { get; } = new();
        public List<NotificationOutboxEntity> Outbox { get; } = new();
    }

    private static (EventReminderProcessor Processor, Captured Captured, Mock<TableClient> Notifications, Mock<TableClient> Outbox)
        Build(
            IEnumerable<EventEntity> events,
            IEnumerable<EventAttendeeEntity> attendees,
            IDictionary<string, NotificationPreferencesEntity?> prefsByUser,
            IEnumerable<NotificationEntity>? existingNotifications = null)
    {
        var eventsTable = new Mock<TableClient>();
        var attendeesTable = new Mock<TableClient>();
        var notificationsTable = new Mock<TableClient>();
        var outboxTable = new Mock<TableClient>();
        var preferencesTable = new Mock<TableClient>();

        eventsTable.Setup(t => t.QueryAsync<EventEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(events.ToAsyncPageable());

        // Attendees query is filtered per-event by PartitionKey; serve only matching rows.
        attendeesTable.Setup(t => t.QueryAsync<EventAttendeeEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns<string, int?, IEnumerable<string>, CancellationToken>((filter, _, _, _) =>
            {
                // Filter form: "PartitionKey eq '<eventId>'"
                var idx = filter.IndexOf("PartitionKey eq '", StringComparison.Ordinal);
                if (idx < 0) return attendees.ToAsyncPageable();
                var start = idx + "PartitionKey eq '".Length;
                var end = filter.IndexOf('\'', start);
                if (end < 0) return attendees.ToAsyncPageable();
                var eventId = filter.Substring(start, end - start);
                return attendees.Where(a => a.PartitionKey == eventId).ToAsyncPageable();
            });

        // Existing notifications used for the SourceEventId dedup scan.
        var existing = existingNotifications?.ToList() ?? new List<NotificationEntity>();
        notificationsTable.Setup(t => t.QueryAsync<NotificationEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns<string, int?, IEnumerable<string>, CancellationToken>((filter, _, _, _) =>
            {
                // Match rows where filter mentions the SourceEventId we're scanning for.
                // Simpler: extract PartitionKey + SourceEventId from filter; both are required.
                string? pk = ExtractEq(filter, "PartitionKey eq '");
                string? src = ExtractEq(filter, "SourceEventId eq '");
                var matches = existing.Where(e =>
                    (pk is null || e.PartitionKey == pk) &&
                    (src is null || e.SourceEventId == src)).ToList();
                return matches.ToAsyncPageable();
            });

        var captured = new Captured();
        notificationsTable.Setup(t => t.AddEntityAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationEntity, CancellationToken>((e, _) => captured.Notifications.Add(e))
            .ReturnsAsync(new Mock<Response>().Object);

        outboxTable.Setup(t => t.AddEntityAsync(It.IsAny<NotificationOutboxEntity>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationOutboxEntity, CancellationToken>((e, _) => captured.Outbox.Add(e))
            .ReturnsAsync(new Mock<Response>().Object);

        preferencesTable.Setup(t => t.GetEntityAsync<NotificationPreferencesEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, IEnumerable<string>, CancellationToken>((userId, _, _, _) =>
            {
                if (prefsByUser.TryGetValue(userId, out var p) && p is not null)
                    return Task.FromResult(Response.FromValue(p, new Mock<Response>().Object));
                throw new RequestFailedException(404, "Not found");
            });

        var processor = new EventReminderProcessor(
            eventsTable.Object, attendeesTable.Object, notificationsTable.Object, outboxTable.Object, preferencesTable.Object,
            NullLogger<EventReminderProcessor>.Instance);

        return (processor, captured, notificationsTable, outboxTable);
    }

    private static string? ExtractEq(string filter, string prefix)
    {
        var idx = filter.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + prefix.Length;
        var end = filter.IndexOf('\'', start);
        if (end < 0) return null;
        return filter.Substring(start, end - start);
    }

    [Fact]
    public async Task RunAsync_EventIn24Hours_RemindsAllAttendees()
    {
        var now = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        var evt = MakeEvent("e1", now.AddHours(24));
        var attendees = new[]
        {
            MakeAttendee("e1", "u1"),
            MakeAttendee("e1", "u2"),
        };
        var prefs = new Dictionary<string, NotificationPreferencesEntity?>
        {
            ["u1"] = MakePrefs("u1"),
            ["u2"] = MakePrefs("u2"),
        };

        var (processor, captured, _, _) = Build(new[] { evt }, attendees, prefs);

        await processor.RunAsync(now, CancellationToken.None);

        Assert.Equal(2, captured.Notifications.Count);
        Assert.All(captured.Notifications, n =>
        {
            Assert.Equal("EventReminder", n.Type);
            Assert.Equal("event-reminder-e1", n.SourceEventId);
            Assert.Contains("e1", n.PayloadJson);
        });
        Assert.Empty(captured.Outbox); // matrix defaults exclude telegram + email
    }

    [Fact]
    public async Task RunAsync_NoEventsInWindow_WritesNothing()
    {
        var now = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        var prefs = new Dictionary<string, NotificationPreferencesEntity?>();
        var (processor, captured, _, _) = Build(Array.Empty<EventEntity>(), Array.Empty<EventAttendeeEntity>(), prefs);

        await processor.RunAsync(now, CancellationToken.None);

        Assert.Empty(captured.Notifications);
        Assert.Empty(captured.Outbox);
    }

    [Fact]
    public async Task RunAsync_EventOutsideWindow_NotRemindedSoon()
    {
        var now = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        // 30 hours away — outside the [now+23h, now+25h) window
        var tooFar = MakeEvent("e1", now.AddHours(30));
        // 10 hours away — also outside
        var tooClose = MakeEvent("e2", now.AddHours(10));
        var attendees = new[] { MakeAttendee("e1", "u1"), MakeAttendee("e2", "u1") };
        var prefs = new Dictionary<string, NotificationPreferencesEntity?> { ["u1"] = MakePrefs("u1") };

        var (processor, captured, _, _) = Build(new[] { tooFar, tooClose }, attendees, prefs);

        await processor.RunAsync(now, CancellationToken.None);

        Assert.Empty(captured.Notifications);
    }

    [Fact]
    public async Task RunAsync_AlreadyReminded_SkipsViaDedup()
    {
        var now = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        var evt = MakeEvent("e1", now.AddHours(24));
        var attendees = new[] { MakeAttendee("e1", "u1") };
        var prefs = new Dictionary<string, NotificationPreferencesEntity?> { ["u1"] = MakePrefs("u1") };

        var existing = new[]
        {
            new NotificationEntity
            {
                PartitionKey = "u1",
                RowKey = "prior",
                NotificationId = "prior",
                UserId = "u1",
                Type = "EventReminder",
                SourceEventId = "event-reminder-e1",
                PayloadJson = "{}",
                CreatedAtUtc = now.AddMinutes(-10),
            }
        };

        var (processor, captured, _, _) = Build(new[] { evt }, attendees, prefs, existing);

        await processor.RunAsync(now, CancellationToken.None);

        Assert.Empty(captured.Notifications);
        Assert.Empty(captured.Outbox);
    }

    [Fact]
    public async Task RunAsync_NoPrefsRow_StillWritesCanonical_NoOutbox()
    {
        var now = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        var evt = MakeEvent("e1", now.AddHours(24));
        var attendees = new[] { MakeAttendee("e1", "u1") };
        // u1 has no prefs row — GetEntityAsync should throw 404
        var prefs = new Dictionary<string, NotificationPreferencesEntity?>();

        var (processor, captured, _, _) = Build(new[] { evt }, attendees, prefs);

        await processor.RunAsync(now, CancellationToken.None);

        Assert.Single(captured.Notifications);
        Assert.Empty(captured.Outbox);
    }

    [Fact]
    public async Task RunAsync_Muted_SkipsOutbox()
    {
        var now = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        var evt = MakeEvent("e1", now.AddHours(24));
        var attendees = new[] { MakeAttendee("e1", "u1") };
        var prefs = new Dictionary<string, NotificationPreferencesEntity?>
        {
            ["u1"] = MakePrefs("u1", telegram: true, email: true, mute: true),
        };

        var (processor, captured, _, _) = Build(new[] { evt }, attendees, prefs);

        await processor.RunAsync(now, CancellationToken.None);

        Assert.Single(captured.Notifications);     // canonical row still written
        Assert.Empty(captured.Outbox);              // mute blocks outbox
    }

    [Fact]
    public async Task RunAsync_TelegramAndEmailEnabled_WritesOutboxRows()
    {
        var now = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        var evt = MakeEvent("e1", now.AddHours(24));
        var attendees = new[] { MakeAttendee("e1", "u1") };
        var prefs = new Dictionary<string, NotificationPreferencesEntity?>
        {
            ["u1"] = MakePrefs("u1", telegram: true, email: true),
        };

        var (processor, captured, _, _) = Build(new[] { evt }, attendees, prefs);

        await processor.RunAsync(now, CancellationToken.None);

        Assert.Single(captured.Notifications);
        Assert.Equal(2, captured.Outbox.Count);
        Assert.Contains(captured.Outbox, r => r.Channel == "Telegram");
        Assert.Contains(captured.Outbox, r => r.Channel == "Email");
        Assert.All(captured.Outbox, r =>
        {
            Assert.Equal("u1", r.UserId);
            Assert.Equal("Immediate", r.Frequency);
            Assert.Equal(captured.Notifications[0].NotificationId, r.NotificationId);
        });
    }

    [Fact]
    public async Task RunAsync_ArchivedEvent_IsSkipped()
    {
        var now = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        var archived = MakeEvent("e1", now.AddHours(24), archived: true);
        var attendees = new[] { MakeAttendee("e1", "u1") };
        var prefs = new Dictionary<string, NotificationPreferencesEntity?> { ["u1"] = MakePrefs("u1") };

        var (processor, captured, _, _) = Build(new[] { archived }, attendees, prefs);

        await processor.RunAsync(now, CancellationToken.None);

        Assert.Empty(captured.Notifications);
    }
}
