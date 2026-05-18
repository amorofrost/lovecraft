namespace Lovecraft.Common.DTOs.Notifications;

public class NotificationAvailabilityDto
{
    public bool TelegramLinked { get; set; }
    public bool EmailVerified { get; set; }
    public bool WebPushSubscribed { get; set; }
}
