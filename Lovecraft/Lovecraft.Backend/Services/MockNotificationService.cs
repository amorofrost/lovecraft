using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services;

public class MockNotificationService : INotificationService
{
    public Task<NotificationDto> CreateAsync(
        string userId, NotificationType type, string? actorId, string payloadJson, string? sourceEventId)
    {
        var dto = new NotificationDto
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Type = type,
            ActorId = actorId,
            PayloadJson = payloadJson ?? "{}",
            CreatedAtUtc = DateTime.UtcNow,
        };
        var list = MockDataStore.Notifications.GetOrAdd(userId, _ => new());
        lock (list)
        {
            list.Add(dto);
            // store SourceEventId in PayloadJson alongside the rest? No — use a parallel dict keyed by notification id.
            DedupKeys[dto.Id] = (type, actorId, sourceEventId);
        }
        return Task.FromResult(Clone(dto));
    }

    public Task EnqueueOutboxAsync(
        string userId, string notificationId, NotificationChannel channel,
        NotificationFrequency frequency, DateTime scheduledForUtc)
    {
        // Mock-mode outbox is a no-op for now (no worker in this phase).
        // Phase C wires the real outbox model. Keep the call so producer logic works.
        return Task.CompletedTask;
    }

    public Task<List<NotificationDto>> ListAsync(string userId, int limit, string? cursor)
    {
        var list = MockDataStore.Notifications.GetOrAdd(userId, _ => new());
        lock (list)
        {
            return Task.FromResult(list
                .Where(n => n.DismissedAtUtc is null)
                .OrderByDescending(n => n.CreatedAtUtc)
                .Take(limit)
                .Select(Clone)
                .ToList());
        }
    }

    public Task<int> UnreadCountAsync(string userId)
    {
        var list = MockDataStore.Notifications.GetOrAdd(userId, _ => new());
        lock (list)
        {
            return Task.FromResult(list.Count(n => n.ReadAtUtc is null && n.DismissedAtUtc is null));
        }
    }

    public Task<bool> MarkReadAsync(string userId, string notificationId)
    {
        var list = MockDataStore.Notifications.GetOrAdd(userId, _ => new());
        lock (list)
        {
            var n = list.FirstOrDefault(x => x.Id == notificationId);
            if (n is null) return Task.FromResult(false);
            n.ReadAtUtc ??= DateTime.UtcNow;
            return Task.FromResult(true);
        }
    }

    public Task<int> MarkAllReadAsync(string userId)
    {
        var list = MockDataStore.Notifications.GetOrAdd(userId, _ => new());
        lock (list)
        {
            var updated = 0;
            foreach (var n in list.Where(n => n.ReadAtUtc is null))
            {
                n.ReadAtUtc = DateTime.UtcNow;
                updated++;
            }
            return Task.FromResult(updated);
        }
    }

    public Task<bool> DismissAsync(string userId, string notificationId)
    {
        var list = MockDataStore.Notifications.GetOrAdd(userId, _ => new());
        lock (list)
        {
            var n = list.FirstOrDefault(x => x.Id == notificationId);
            if (n is null) return Task.FromResult(false);
            n.DismissedAtUtc ??= DateTime.UtcNow;
            return Task.FromResult(true);
        }
    }

    public Task<List<NotificationDto>> RecentForDedupAsync(
        string userId, NotificationType type, string? actorId, string? sourceEventId, int withinSeconds)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-withinSeconds);
        var list = MockDataStore.Notifications.GetOrAdd(userId, _ => new());
        lock (list)
        {
            return Task.FromResult(list
                .Where(n => n.CreatedAtUtc >= cutoff)
                .Where(n => DedupKeys.TryGetValue(n.Id, out var key)
                            && key.Type == type
                            && key.ActorId == actorId
                            && key.SourceEventId == sourceEventId)
                .Select(Clone)
                .ToList());
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string, (NotificationType Type, string? ActorId, string? SourceEventId)> DedupKeys = new();

    private static NotificationDto Clone(NotificationDto src) => new()
    {
        Id = src.Id,
        UserId = src.UserId,
        Type = src.Type,
        ActorId = src.ActorId,
        ActorName = src.ActorName,
        ActorAvatar = src.ActorAvatar,
        PayloadJson = src.PayloadJson,
        CreatedAtUtc = src.CreatedAtUtc,
        ReadAtUtc = src.ReadAtUtc,
        DismissedAtUtc = src.DismissedAtUtc,
        DigestGroupId = src.DigestGroupId,
    };
}
