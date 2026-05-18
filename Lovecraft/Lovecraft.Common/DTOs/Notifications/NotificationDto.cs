using Lovecraft.Common.Enums;

namespace Lovecraft.Common.DTOs.Notifications;

public class NotificationDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    /// <summary>User id of the actor (sender of like, message, reply etc.). Null for system notifications.</summary>
    public string? ActorId { get; set; }
    public string? ActorName { get; set; }
    public string? ActorAvatar { get; set; }
    /// <summary>Type-specific payload, serialized as JSON. Shape varies per Type.</summary>
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
    /// <summary>Set when this row was rolled into a digest send so the worker doesn't redeliver.</summary>
    public string? DigestGroupId { get; set; }
}

public class NotificationListResponseDto
{
    public List<NotificationDto> Items { get; set; } = new();
    public string? NextCursor { get; set; }
}

public class UnreadCountResponseDto
{
    public int Count { get; set; }
}
