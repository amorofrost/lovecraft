using WebPush;

namespace Lovecraft.Backend.Services.Notifications;

/// <summary>
/// Production adapter delegating to the real WebPushClient.
/// Tests mock WebPush.IWebPushClient directly (that interface already exists in the WebPush package).
/// </summary>
public class WebPushClientAdapter : WebPush.IWebPushClient
{
    private readonly WebPushClient _inner = new();

    public void SetGcmApiKey(string gcmApiKey) => _inner.SetGcmApiKey(gcmApiKey);

    public void SetVapidDetails(VapidDetails vapidDetails) => _inner.SetVapidDetails(vapidDetails);

    public void SetVapidDetails(string subject, string publicKey, string privateKey)
        => _inner.SetVapidDetails(subject, publicKey, privateKey);

    public System.Net.Http.HttpRequestMessage GenerateRequestDetails(
        PushSubscription subscription, string payload, System.Collections.Generic.Dictionary<string, object>? options = null)
        => _inner.GenerateRequestDetails(subscription, payload, options!);

    public void SendNotification(PushSubscription subscription, string payload,
        System.Collections.Generic.Dictionary<string, object>? options = null)
        => _inner.SendNotification(subscription, payload, options!);

    public void SendNotification(PushSubscription subscription, string payload, VapidDetails vapidDetails)
        => _inner.SendNotification(subscription, payload, vapidDetails);

    public void SendNotification(PushSubscription subscription, string payload, string gcmApiKey)
        => _inner.SendNotification(subscription, payload, gcmApiKey);

    public Task SendNotificationAsync(PushSubscription subscription, string payload,
        System.Collections.Generic.Dictionary<string, object>? options, CancellationToken cancellationToken = default)
        => _inner.SendNotificationAsync(subscription, payload, options!, cancellationToken);

    public Task SendNotificationAsync(PushSubscription subscription, string payload, VapidDetails vapidDetails,
        CancellationToken cancellationToken = default)
        => _inner.SendNotificationAsync(subscription, payload, vapidDetails, cancellationToken);

    public Task SendNotificationAsync(PushSubscription subscription, string payload, string gcmApiKey,
        CancellationToken cancellationToken = default)
        => _inner.SendNotificationAsync(subscription, payload, gcmApiKey, cancellationToken);

    public void Dispose() => _inner.Dispose();
}
