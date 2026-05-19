using Lovecraft.Common.DTOs.Notifications;

namespace Lovecraft.Backend.Services.Notifications;

public interface IWebPushDispatcher
{
    /// <summary>
    /// Fires Web Push to all of the user's subscribed devices. Best-effort: errors are logged
    /// and dead subscriptions (HTTP 404/410) are deleted. Caller can fire-and-forget.
    /// </summary>
    Task DispatchAsync(string userId, NotificationDto notification);
}
