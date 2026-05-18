using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Dispatchers;
using Lovecraft.NotificationsWorker.Entities;
using Lovecraft.NotificationsWorker.Models;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Services;

public class OutboxProcessor : IOutboxProcessor
{
    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
    };

    private const int MaxAttempts = 5;

    private readonly TableClient _outbox;
    private readonly TableClient _notifications;
    private readonly ITelegramDispatcher _telegram;
    private readonly IEmailDispatcher _email;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        TableClient outbox,
        TableClient notifications,
        ITelegramDispatcher telegram,
        IEmailDispatcher email,
        ILogger<OutboxProcessor> logger)
    {
        _outbox = outbox;
        _notifications = notifications;
        _telegram = telegram;
        _email = email;
        _logger = logger;
    }

    public async Task ProcessChannelAsync(string channel, CancellationToken ct)
    {
        var pendingPartition = NotificationOutboxEntity.PendingPartition(channel);
        var now = DateTime.UtcNow;
        var rowKeyCeiling = $"{now:yyyy-MM-ddTHH:mm:ss}_~";    // "~" sorts after all digit/hex chars

        var filter = $"PartitionKey eq '{pendingPartition}' and RowKey le '{rowKeyCeiling}'";

        await foreach (var row in _outbox.QueryAsync<NotificationOutboxEntity>(filter).WithCancellation(ct))
        {
            // Frequency=Hourly/Daily rows are handled by DigestWorker. The DispatcherWorker only processes Immediate rows.
            if (row.Frequency != "Immediate") continue;

            try
            {
                var notification = await LoadNotificationAsync(row.UserId, row.NotificationId, ct);
                if (notification is null)
                {
                    _logger.LogWarning("Outbox row references unknown notification {NotificationId}; marking as PermanentError",
                        row.NotificationId);
                    await MoveToDeadAsync(row, "Notification not found", ct);
                    continue;
                }

                var result = channel switch
                {
                    "Telegram" => await _telegram.DispatchAsync(notification, ct),
                    "Email" => await _email.DispatchAsync(notification, ct),
                    _ => DispatchResult.PermanentError,
                };

                switch (result)
                {
                    case DispatchResult.Delivered:
                        await MoveToDoneAsync(row, ct);
                        break;
                    case DispatchResult.RetryableError:
                        await RescheduleAsync(row, ct);
                        break;
                    case DispatchResult.PermanentError:
                        await MoveToDeadAsync(row, "PermanentError from dispatcher", ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox row {RowKey}", row.RowKey);
                try { await RescheduleAsync(row, ct); } catch { /* swallow — leaving row in PENDING with old RowKey is fine for next tick */ }
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

    private async Task MoveToDoneAsync(NotificationOutboxEntity row, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var doneEntity = new NotificationOutboxEntity
        {
            PartitionKey = NotificationOutboxEntity.DonePartition(row.Channel, now),
            RowKey = row.RowKey,
            UserId = row.UserId,
            NotificationId = row.NotificationId,
            Channel = row.Channel,
            Frequency = row.Frequency,
            ScheduledForUtc = row.ScheduledForUtc,
            Attempts = row.Attempts,
            DeliveredAtUtc = now,
        };
        await _outbox.AddEntityAsync(doneEntity, ct);
        await _outbox.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, ct);
    }

    private async Task MoveToDeadAsync(NotificationOutboxEntity row, string error, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var deadEntity = new NotificationOutboxEntity
        {
            PartitionKey = NotificationOutboxEntity.DeadPartition(row.Channel, now),
            RowKey = row.RowKey,
            UserId = row.UserId,
            NotificationId = row.NotificationId,
            Channel = row.Channel,
            Frequency = row.Frequency,
            ScheduledForUtc = row.ScheduledForUtc,
            Attempts = row.Attempts,
            LastErrorMessage = error,
        };
        await _outbox.AddEntityAsync(deadEntity, ct);
        await _outbox.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, ct);
    }

    private async Task RescheduleAsync(NotificationOutboxEntity row, CancellationToken ct)
    {
        var newAttempts = row.Attempts + 1;
        if (newAttempts >= MaxAttempts)
        {
            row.Attempts = newAttempts;
            await MoveToDeadAsync(row, $"Exceeded {MaxAttempts} attempts", ct);
            return;
        }

        var backoffIdx = Math.Min(newAttempts - 1, Backoff.Length - 1);   // attempts 1..N use Backoff[0..N-1]
        var rescheduledFor = DateTime.UtcNow + Backoff[backoffIdx];

        var rescheduled = new NotificationOutboxEntity
        {
            PartitionKey = row.PartitionKey,    // still PENDING
            RowKey = NotificationOutboxEntity.GetRowKey(rescheduledFor, row.NotificationId),
            UserId = row.UserId,
            NotificationId = row.NotificationId,
            Channel = row.Channel,
            Frequency = row.Frequency,
            ScheduledForUtc = rescheduledFor,
            Attempts = newAttempts,
            LastErrorMessage = "Retryable error — backoff scheduled",
        };
        await _outbox.AddEntityAsync(rescheduled, ct);
        await _outbox.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, ct);
    }
}
