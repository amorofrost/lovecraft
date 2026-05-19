using Azure;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Dispatchers;
using Lovecraft.NotificationsWorker.Entities;
using Lovecraft.NotificationsWorker.Models;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Services;

public class DigestProcessor : IDigestProcessor
{
    private static readonly string[] DigestChannels = { "Telegram", "Email" };

    private readonly TableClient _outbox;
    private readonly TableClient _notifications;
    private readonly TableClient _preferences;
    private readonly ITelegramDispatcher _telegram;
    private readonly IEmailDispatcher _email;
    private readonly ILogger<DigestProcessor> _logger;

    public DigestProcessor(
        TableClient outbox,
        TableClient notifications,
        TableClient preferences,
        ITelegramDispatcher telegram,
        IEmailDispatcher email,
        ILogger<DigestProcessor> logger)
    {
        _outbox = outbox;
        _notifications = notifications;
        _preferences = preferences;
        _telegram = telegram;
        _email = email;
        _logger = logger;
    }

    public async Task ProcessAsync(DateTime now, CancellationToken ct)
    {
        foreach (var channel in DigestChannels)
        {
            var pendingPartition = NotificationOutboxEntity.PendingPartition(channel);
            var rowKeyCeiling = $"{now:yyyy-MM-ddTHH:mm:ss}_~";    // "~" sorts after all digit/hex chars
            var filter = $"PartitionKey eq '{pendingPartition}' and RowKey le '{rowKeyCeiling}'";

            // Group rows by userId
            var byUser = new Dictionary<string, List<NotificationOutboxEntity>>();
            await foreach (var row in _outbox.QueryAsync<NotificationOutboxEntity>(filter).WithCancellation(ct))
            {
                if (row.Frequency != "Hourly" && row.Frequency != "Daily") continue;
                if (!byUser.TryGetValue(row.UserId, out var list))
                {
                    list = new List<NotificationOutboxEntity>();
                    byUser[row.UserId] = list;
                }
                list.Add(row);
            }

            foreach (var (userId, rows) in byUser)
            {
                // For Daily rows, check user's DailyDigestHourUtc
                int dailyHour = 9;
                try
                {
                    var prefsResp = await _preferences.GetEntityAsync<NotificationPreferencesEntity>(userId, "INDEX", cancellationToken: ct);
                    dailyHour = prefsResp.Value.DailyDigestHourUtc;
                }
                catch (RequestFailedException ex) when (ex.Status == 404) { /* defaults */ }

                var eligible = rows.Where(r =>
                    r.Frequency == "Hourly" ||
                    (r.Frequency == "Daily" && now.Hour == dailyHour)).ToList();

                if (eligible.Count == 0) continue;

                // Load each notification
                var members = new List<NotificationModel>();
                foreach (var row in eligible)
                {
                    var notif = await LoadNotificationAsync(row.UserId, row.NotificationId, ct);
                    if (notif is not null) members.Add(notif);
                }

                if (members.Count == 0) continue;

                // Build digest model — Phase F: Email uses full DispatchDigestAsync;
                // Telegram still uses single-member trick (real digest impl deferred).
                var digest = new DigestModel(userId, members);

                var first = members[0];
                DispatchResult result;
                if (channel == "Email")
                {
                    result = await _email.DispatchDigestAsync(digest, ct);
                }
                else if (channel == "Telegram")
                {
                    // Phase F: Telegram digests still use single-member trick (real digest impl deferred).
                    result = await _telegram.DispatchAsync(first, ct);
                }
                else
                {
                    result = DispatchResult.PermanentError;
                }

                if (result == DispatchResult.Delivered)
                {
                    foreach (var row in eligible)
                    {
                        var now2 = DateTime.UtcNow;
                        var doneRow = new NotificationOutboxEntity
                        {
                            PartitionKey = NotificationOutboxEntity.DonePartition(row.Channel, now2),
                            RowKey = row.RowKey,
                            UserId = row.UserId,
                            NotificationId = row.NotificationId,
                            Channel = row.Channel,
                            Frequency = row.Frequency,
                            ScheduledForUtc = row.ScheduledForUtc,
                            Attempts = row.Attempts,
                            DeliveredAtUtc = now2,
                        };
                        await _outbox.AddEntityAsync(doneRow, ct);
                        await _outbox.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, ct);
                    }
                }
                else
                {
                    _logger.LogWarning("Digest dispatch for user {UserId} channel {Channel} returned {Result}; rows remain pending",
                        userId, channel, result);
                }
            }
        }
    }

    private async Task<NotificationModel?> LoadNotificationAsync(string userId, string notificationId, CancellationToken ct)
    {
        var filter = $"PartitionKey eq '{userId}' and NotificationId eq '{notificationId}'";
        await foreach (var entity in _notifications.QueryAsync<NotificationEntity>(filter, maxPerPage: 1).WithCancellation(ct))
        {
            return new NotificationModel(
                entity.NotificationId, entity.UserId, entity.Type,
                entity.ActorId, entity.PayloadJson, entity.CreatedAtUtc);
        }
        return null;
    }
}
