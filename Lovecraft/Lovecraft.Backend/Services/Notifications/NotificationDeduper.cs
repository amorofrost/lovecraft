using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services.Notifications;

/// <summary>
/// 60-second same-source-event window. Producer must pass a stable <c>sourceEventId</c>
/// (messageId / likeId / replyId etc.) for dedup to apply. A null sourceEventId
/// always returns false — caller is expected to be at-most-once by construction.
/// </summary>
public class NotificationDeduper
{
    private const int WindowSeconds = 60;
    private readonly INotificationService _notifications;

    public NotificationDeduper(INotificationService notifications)
    {
        _notifications = notifications;
    }

    public async Task<bool> IsDuplicateAsync(string userId, NotificationType type, string? actorId, string? sourceEventId)
    {
        if (sourceEventId is null) return false;
        var hits = await _notifications.RecentForDedupAsync(userId, type, actorId, sourceEventId, WindowSeconds);
        return hits.Count > 0;
    }
}
