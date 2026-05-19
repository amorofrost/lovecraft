namespace Lovecraft.Common.DTOs.Notifications;

/// <summary>
/// JSON payload sent through Web Push to the browser service worker.
/// `sw.js` reads it from event.data.json() and calls showNotification.
/// </summary>
public class WebPushNotificationDto
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Url { get; set; } = "/";
    public string? Icon { get; set; }
}
