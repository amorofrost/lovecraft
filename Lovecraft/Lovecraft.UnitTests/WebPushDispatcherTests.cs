using System.Net;
using System.Net.Http;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebPush;
using Xunit;

namespace Lovecraft.UnitTests;

public class WebPushDispatcherTests
{
    private static NotificationDto SampleNotification() => new()
    {
        Id = "n1",
        UserId = "u1",
        Type = NotificationType.LikeReceived,
        ActorId = "actor-1",
        PayloadJson = "{\"likeId\":\"l1\",\"anonymous\":false}",
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static WebPushSubscriptionDto MakeSub(string deviceId) => new()
    {
        DeviceId = deviceId,
        Endpoint = "https://push.example/" + deviceId,
        P256dh = "p256dh-key-base64url",
        Auth = "auth-key-base64url",
        UserAgent = "test",
        CreatedAtUtc = DateTime.UtcNow,
        LastSeenAtUtc = DateTime.UtcNow,
    };

    /// <summary>
    /// Construct a WebPushException with the given status code.
    /// WebPush 1.0.13 constructor: WebPushException(string message, PushSubscription pushSubscription, HttpResponseMessage responseMessage)
    /// StatusCode is derived from HttpResponseMessage.StatusCode.
    /// </summary>
    private static WebPushException MakeWebPushException(HttpStatusCode status)
        => new WebPushException("test", new PushSubscription(), new HttpResponseMessage(status));

    private static (WebPushDispatcher dispatcher, Mock<IPushSubscriptionService> push, Mock<WebPush.IWebPushClient> client)
        Build(Mock<IPushSubscriptionService>? push = null, Mock<WebPush.IWebPushClient>? client = null)
    {
        push ??= new Mock<IPushSubscriptionService>();
        client ??= new Mock<WebPush.IWebPushClient>();

        var renderer = new WebPushPayloadRenderer(NullLogger<WebPushPayloadRenderer>.Instance);
        var dispatcher = new WebPushDispatcher(
            client.Object, push.Object, renderer,
            publicKey: "test-public-key", privateKey: "test-private-key", subject: "mailto:test@example.com",
            NullLogger<WebPushDispatcher>.Instance);
        return (dispatcher, push, client);
    }

    [Fact]
    public async Task No_subscriptions_does_nothing()
    {
        var push = new Mock<IPushSubscriptionService>();
        push.Setup(p => p.ListAsync("u1")).ReturnsAsync(new List<WebPushSubscriptionDto>());
        var (dispatcher, _, client) = Build(push);

        await dispatcher.DispatchAsync("u1", SampleNotification());

        client.Verify(c => c.SendNotificationAsync(
            It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Successful_send_does_not_unsubscribe()
    {
        var push = new Mock<IPushSubscriptionService>();
        push.Setup(p => p.ListAsync("u1")).ReturnsAsync(new List<WebPushSubscriptionDto> { MakeSub("dev1") });
        var client = new Mock<IWebPushClient>();
        client.Setup(c => c.SendNotificationAsync(
                It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var (dispatcher, _, _) = Build(push, client);

        await dispatcher.DispatchAsync("u1", SampleNotification());

        push.Verify(p => p.UnsubscribeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Http_404_deletes_subscription()
    {
        var push = new Mock<IPushSubscriptionService>();
        push.Setup(p => p.ListAsync("u1")).ReturnsAsync(new List<WebPushSubscriptionDto> { MakeSub("dev1") });
        push.Setup(p => p.UnsubscribeAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var client = new Mock<IWebPushClient>();
        client.Setup(c => c.SendNotificationAsync(
                It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeWebPushException(HttpStatusCode.NotFound));
        var (dispatcher, _, _) = Build(push, client);

        await dispatcher.DispatchAsync("u1", SampleNotification());

        push.Verify(p => p.UnsubscribeAsync("u1", "dev1"), Times.Once);
    }

    [Fact]
    public async Task Http_410_deletes_subscription()
    {
        var push = new Mock<IPushSubscriptionService>();
        push.Setup(p => p.ListAsync("u1")).ReturnsAsync(new List<WebPushSubscriptionDto> { MakeSub("dev1") });
        push.Setup(p => p.UnsubscribeAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var client = new Mock<IWebPushClient>();
        client.Setup(c => c.SendNotificationAsync(
                It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeWebPushException(HttpStatusCode.Gone));
        var (dispatcher, _, _) = Build(push, client);

        await dispatcher.DispatchAsync("u1", SampleNotification());

        push.Verify(p => p.UnsubscribeAsync("u1", "dev1"), Times.Once);
    }

    [Fact]
    public async Task Multiple_devices_all_attempted()
    {
        var push = new Mock<IPushSubscriptionService>();
        push.Setup(p => p.ListAsync("u1")).ReturnsAsync(new List<WebPushSubscriptionDto>
        {
            MakeSub("dev1"),
            MakeSub("dev2"),
            MakeSub("dev3"),
        });
        var client = new Mock<IWebPushClient>();
        client.Setup(c => c.SendNotificationAsync(
                It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var (dispatcher, _, _) = Build(push, client);

        await dispatcher.DispatchAsync("u1", SampleNotification());

        client.Verify(c => c.SendNotificationAsync(
            It.IsAny<PushSubscription>(), It.IsAny<string>(), It.IsAny<VapidDetails>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}
