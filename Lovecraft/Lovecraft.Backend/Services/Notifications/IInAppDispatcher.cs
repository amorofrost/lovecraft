using Lovecraft.Common.DTOs.Notifications;

namespace Lovecraft.Backend.Services.Notifications;

public interface IInAppDispatcher
{
    Task DispatchAsync(string userId, NotificationDto notification);
}
