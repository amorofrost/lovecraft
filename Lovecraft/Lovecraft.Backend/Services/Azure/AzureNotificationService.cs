using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Services.Azure;

public class AzureNotificationService : INotificationService
{
    private readonly TableClient _notifications;
    private readonly TableClient _outbox;
    private readonly ILogger<AzureNotificationService> _logger;

    public AzureNotificationService(TableClient notifications, TableClient outbox, ILogger<AzureNotificationService> logger)
    {
        _notifications = notifications;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<NotificationDto> CreateAsync(
        string userId, NotificationType type, string? actorId, string payloadJson, string? sourceEventId)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var entity = new NotificationEntity
        {
            PartitionKey = NotificationEntity.GetPartitionKey(userId),
            RowKey = NotificationEntity.GetRowKey(id, now),
            NotificationId = id,
            UserId = userId,
            Type = type.ToString(),
            ActorId = actorId,
            PayloadJson = payloadJson ?? "{}",
            CreatedAtUtc = now,
            SourceEventId = sourceEventId,
            IsRead = false,
            IsDismissed = false,
        };
        await _notifications.AddEntityAsync(entity);
        return ToDto(entity);
    }

    public async Task EnqueueOutboxAsync(
        string userId, string notificationId, NotificationChannel channel,
        NotificationFrequency frequency, DateTime scheduledForUtc)
    {
        var entity = new NotificationOutboxEntity
        {
            PartitionKey = NotificationOutboxEntity.PendingPartition(channel.ToString()),
            RowKey = NotificationOutboxEntity.GetRowKey(scheduledForUtc, notificationId),
            UserId = userId,
            NotificationId = notificationId,
            Channel = channel.ToString(),
            Frequency = frequency.ToString(),
            ScheduledForUtc = scheduledForUtc,
        };
        await _outbox.AddEntityAsync(entity);
    }

    public async Task<List<NotificationDto>> ListAsync(string userId, int limit, string? cursor)
    {
        var filter = $"PartitionKey eq '{userId}' and IsDismissed eq false";
        if (!string.IsNullOrEmpty(cursor))
            filter += $" and RowKey gt '{cursor.Replace("'", "''")}'";

        var results = new List<NotificationDto>();
        await foreach (var page in _notifications.QueryAsync<NotificationEntity>(filter, maxPerPage: limit).AsPages())
        {
            results.AddRange(page.Values.Select(ToDto));
            if (results.Count >= limit) break;
        }
        return results.Take(limit).ToList();
    }

    public async Task<int> UnreadCountAsync(string userId)
    {
        var filter = $"PartitionKey eq '{userId}' and IsRead eq false and IsDismissed eq false";
        var count = 0;
        await foreach (var _ in _notifications.QueryAsync<NotificationEntity>(filter, select: new[] { "RowKey" }))
            count++;
        return count;
    }

    public async Task<bool> MarkReadAsync(string userId, string notificationId)
    {
        var entity = await FindByIdAsync(userId, notificationId);
        if (entity is null) return false;
        if (entity.ReadAtUtc.HasValue) return true;
        entity.ReadAtUtc = DateTime.UtcNow;
        entity.IsRead = true;
        await _notifications.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
        return true;
    }

    public async Task<int> MarkAllReadAsync(string userId)
    {
        var filter = $"PartitionKey eq '{userId}' and IsRead eq false and IsDismissed eq false";
        var updated = 0;
        var now = DateTime.UtcNow;
        await foreach (var entity in _notifications.QueryAsync<NotificationEntity>(filter))
        {
            entity.ReadAtUtc = now;
            entity.IsRead = true;
            try
            {
                await _notifications.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
                updated++;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                _logger.LogInformation("ETag conflict marking read for {NotificationId} (skipping)", entity.NotificationId);
            }
        }
        return updated;
    }

    public async Task<bool> DismissAsync(string userId, string notificationId)
    {
        var entity = await FindByIdAsync(userId, notificationId);
        if (entity is null) return false;
        if (entity.DismissedAtUtc.HasValue) return true;
        entity.DismissedAtUtc = DateTime.UtcNow;
        entity.IsDismissed = true;
        await _notifications.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
        return true;
    }

    public async Task<List<NotificationDto>> RecentForDedupAsync(
        string userId, NotificationType type, string? actorId, string? sourceEventId, int withinSeconds)
    {
        var since = DateTime.UtcNow.AddSeconds(-withinSeconds);
        var filter = $"PartitionKey eq '{userId}' and CreatedAtUtc ge datetime'{since:O}'";
        var hits = new List<NotificationDto>();
        await foreach (var e in _notifications.QueryAsync<NotificationEntity>(filter))
        {
            if (e.Type != type.ToString()) continue;
            if (e.ActorId != actorId) continue;
            if (e.SourceEventId != sourceEventId) continue;
            hits.Add(ToDto(e));
        }
        return hits;
    }

    private async Task<NotificationEntity?> FindByIdAsync(string userId, string notificationId)
    {
        var filter = $"PartitionKey eq '{userId}' and NotificationId eq '{notificationId}'";
        await foreach (var e in _notifications.QueryAsync<NotificationEntity>(filter, maxPerPage: 1))
            return e;
        return null;
    }

    private static NotificationDto ToDto(NotificationEntity e) => new()
    {
        Id = e.NotificationId,
        UserId = e.UserId,
        Type = Enum.Parse<NotificationType>(e.Type),
        ActorId = e.ActorId,
        PayloadJson = e.PayloadJson,
        CreatedAtUtc = e.CreatedAtUtc,
        ReadAtUtc = e.ReadAtUtc,
        DismissedAtUtc = e.DismissedAtUtc,
        DigestGroupId = e.DigestGroupId,
        Cursor = e.RowKey,
    };
}
