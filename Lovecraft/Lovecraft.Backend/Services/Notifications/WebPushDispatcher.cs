using System.Net;
using System.Text.Json;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Notifications;
using Microsoft.Extensions.Logging;
using WebPush;

namespace Lovecraft.Backend.Services.Notifications;

public class WebPushDispatcher : IWebPushDispatcher
{
    private readonly WebPush.IWebPushClient _client;
    private readonly IPushSubscriptionService _pushService;
    private readonly IWebPushPayloadRenderer _renderer;
    private readonly VapidDetails? _vapidDetails;
    private readonly ILogger<WebPushDispatcher> _logger;

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public WebPushDispatcher(
        WebPush.IWebPushClient client,
        IPushSubscriptionService pushService,
        IWebPushPayloadRenderer renderer,
        string? publicKey, string? privateKey, string? subject,
        ILogger<WebPushDispatcher> logger)
    {
        _client = client;
        _pushService = pushService;
        _renderer = renderer;
        _logger = logger;

        if (!string.IsNullOrEmpty(publicKey) && !string.IsNullOrEmpty(privateKey) && !string.IsNullOrEmpty(subject))
        {
            _vapidDetails = new VapidDetails(subject, publicKey, privateKey);
        }
        else
        {
            _logger.LogWarning("WebPush VAPID credentials not configured; WebPushDispatcher will be a no-op");
        }
    }

    public async Task DispatchAsync(string userId, NotificationDto notification)
    {
        if (_vapidDetails is null)
        {
            _logger.LogDebug("WebPush VAPID not configured; skipping dispatch for {NotificationId}", notification.Id);
            return;
        }

        var subscriptions = await _pushService.ListAsync(userId);
        if (subscriptions.Count == 0) return;

        var payload = _renderer.Render(notification);
        var payloadJson = JsonSerializer.Serialize(payload, CamelCase);

        foreach (var sub in subscriptions)
        {
            var pushSub = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
            try
            {
                await _client.SendNotificationAsync(pushSub, payloadJson, _vapidDetails);
            }
            catch (WebPushException ex) when (
                ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.Gone)
            {
                _logger.LogInformation(
                    "Push subscription {DeviceId} for user {UserId} is gone (HTTP {Status}); removing",
                    sub.DeviceId, userId, (int)ex.StatusCode);
                try
                {
                    await _pushService.UnsubscribeAsync(userId, sub.DeviceId);
                }
                catch (Exception delEx)
                {
                    _logger.LogWarning(delEx,
                        "Failed to clean up dead push subscription {DeviceId} for user {UserId}",
                        sub.DeviceId, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "WebPush send failed for user {UserId} device {DeviceId} (continuing)",
                    userId, sub.DeviceId);
            }
        }
    }
}
