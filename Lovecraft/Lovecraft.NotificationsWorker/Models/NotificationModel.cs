namespace Lovecraft.NotificationsWorker.Models;

/// <summary>
/// Lightweight notification representation used by worker dispatchers.
/// Built from `NotificationEntity` reads.
/// </summary>
public record NotificationModel(
    string NotificationId,
    string UserId,
    string Type,
    string? ActorId,
    string PayloadJson,
    DateTime CreatedAtUtc);

public enum DispatchResult
{
    Delivered,
    RetryableError,
    PermanentError,
}
