using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services.Notifications;

public interface INotificationProducer
{
    /// <summary>
    /// Produce a notification. Returns the written DTO, or null if suppressed
    /// (self-action, dedup hit, master mute, in-chat suppression, etc.).
    /// </summary>
    Task<NotificationDto?> ProduceAsync(
        string recipientUserId,
        NotificationType type,
        string? actorId,
        string payloadJson,
        string? sourceEventId,
        string? presenceGroup = null);
}
