using Lovecraft.Common.DTOs.Notifications;

namespace Lovecraft.Backend.Services.Notifications;

public interface IWebPushPayloadRenderer
{
    WebPushNotificationDto Render(NotificationDto notification);
}
