using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lovecraft.UnitTests;

public class WebPushPayloadRendererTests
{
    private readonly WebPushPayloadRenderer _renderer = new(NullLogger<WebPushPayloadRenderer>.Instance);

    private static NotificationDto MakeNotification(NotificationType type, string payloadJson, string? actorId = null) =>
        new()
        {
            Id = "n1",
            UserId = "u1",
            Type = type,
            ActorId = actorId,
            PayloadJson = payloadJson,
            CreatedAtUtc = DateTime.UtcNow,
        };

    [Fact]
    public void MessageReceived_uses_payload_preview()
    {
        var notif = MakeNotification(NotificationType.MessageReceived,
            "{\"chatId\":\"c1\",\"messageId\":\"m1\",\"preview\":\"hello\"}");

        var result = _renderer.Render(notif);

        Assert.Equal("New message", result.Title);
        Assert.Equal("hello", result.Body);
        Assert.Equal("/talks?chat=c1", result.Url);
    }

    [Fact]
    public void LikeReceived_routes_to_friends_with_actor()
    {
        var notif = MakeNotification(NotificationType.LikeReceived,
            "{\"likeId\":\"l1\",\"anonymous\":false}",
            actorId: "actor-1");

        var result = _renderer.Render(notif);

        Assert.Equal("/friends?userId=actor-1", result.Url);
    }

    [Fact]
    public void CommunityBroadcast_disallows_off_domain_absolute_urls()
    {
        var notif = MakeNotification(NotificationType.CommunityBroadcast,
            "{\"title\":\"X\",\"body\":\"Y\",\"link\":\"https://evil.example/phish\"}");

        var result = _renderer.Render(notif);

        Assert.DoesNotContain("evil.example", result.Url);
        Assert.StartsWith("/", result.Url);   // Falls back to /aloevera safe default
    }

    [Fact]
    public void Malformed_payload_renders_safely()
    {
        var notif = MakeNotification(NotificationType.MessageReceived, "not-valid-json");

        var result = _renderer.Render(notif);

        Assert.NotEmpty(result.Title);
        Assert.NotNull(result.Body);
        Assert.NotEmpty(result.Url);
    }
}
