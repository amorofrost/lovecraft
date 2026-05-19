using Azure;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Entities;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Services;

public class OutboxJanitor : IOutboxJanitor
{
    private const int OutboxRetentionDays = 30;
    private const int NotificationRetentionDays = 90;

    private readonly TableClient _outbox;
    private readonly TableClient _notifications;
    private readonly ILogger<OutboxJanitor> _logger;

    public OutboxJanitor(TableClient outbox, TableClient notifications, ILogger<OutboxJanitor> logger)
    {
        _outbox = outbox;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task RunAsync(DateTime now, CancellationToken ct)
    {
        await CleanOutboxAsync(now, ct);
        await CleanNotificationsAsync(now, ct);
    }

    private async Task CleanOutboxAsync(DateTime now, CancellationToken ct)
    {
        // Scan all DONE_/DEAD_ partitions; check PartitionKey for embedded date suffix.
        // We can't query by partition prefix in OData, so we filter client-side after fetching the partition list — costly but rare (once/day).
        var cutoff = now.AddDays(-OutboxRetentionDays);

        var filter = "PartitionKey ge 'OUTBOX_' and PartitionKey lt 'OUTBOX`'";    // 'OUTBOX_' through next char before `
        var count = 0;
        await foreach (var row in _outbox.QueryAsync<NotificationOutboxEntity>(filter).WithCancellation(ct))
        {
            // PartitionKey format: OUTBOX_{channel}_{status}_{yyyy-MM-dd}
            // Status: PENDING | DONE | DEAD — skip PENDING (active queue), check DONE/DEAD
            if (row.PartitionKey.Contains("_PENDING")) continue;
            var lastUnderscore = row.PartitionKey.LastIndexOf('_');
            if (lastUnderscore < 0) continue;
            var datePart = row.PartitionKey.Substring(lastUnderscore + 1);
            if (!DateTime.TryParse(datePart, out var partitionDate)) continue;
            if (partitionDate >= cutoff) continue;

            try
            {
                await _outbox.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, ct);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old outbox row {PK}/{RK}", row.PartitionKey, row.RowKey);
            }
        }
        _logger.LogInformation("OutboxJanitor: deleted {Count} old outbox rows (cutoff {Cutoff})", count, cutoff);
    }

    private async Task CleanNotificationsAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-NotificationRetentionDays);
        var filter = $"CreatedAtUtc lt datetime'{cutoff:O}'";
        var count = 0;
        await foreach (var row in _notifications.QueryAsync<NotificationEntity>(filter).WithCancellation(ct))
        {
            try
            {
                await _notifications.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, ct);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old notification row {PK}/{RK}", row.PartitionKey, row.RowKey);
            }
        }
        _logger.LogInformation("OutboxJanitor: deleted {Count} old notifications (cutoff {Cutoff})", count, cutoff);
    }
}
