using Lovecraft.NotificationsWorker.Models;
using Lovecraft.NotificationsWorker.Renderers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class EmailDigestRendererTests
{
    private readonly EmailDigestRenderer _renderer = new(
        unsubscribeBaseUrl: "https://aloeve.club",
        appBaseUrl: "https://aloeve.club",
        NullLogger<EmailDigestRenderer>.Instance);

    private static NotificationModel MakeNotification(string type, string payloadJson, string? actorId = null) =>
        new("n1", "u1", type, actorId, payloadJson, DateTime.UtcNow);

    [Fact]
    public void RenderSingle_MessageReceived_includes_preview_in_body()
    {
        var notif = MakeNotification("MessageReceived",
            "{\"chatId\":\"c1\",\"messageId\":\"m1\",\"preview\":\"hello there\"}");

        var result = _renderer.RenderSingle(notif, "unsub-token-abc");

        Assert.Contains("hello there", result.HtmlBody);
        Assert.Contains("hello there", result.PlainTextBody);
        Assert.NotEmpty(result.Subject);
    }

    [Fact]
    public void RenderDigest_groups_by_type_with_section_headers()
    {
        var digest = new DigestModel("u1", new List<NotificationModel>
        {
            MakeNotification("LikeReceived", "{\"likeId\":\"l1\",\"anonymous\":false}"),
            MakeNotification("LikeReceived", "{\"likeId\":\"l2\",\"anonymous\":true}"),
            MakeNotification("MatchCreated", "{\"matchId\":\"m1\"}", actorId: "actor-1"),
        });

        var result = _renderer.RenderDigest(digest, "unsub-token-abc");

        Assert.Contains("New likes", result.HtmlBody);
        Assert.Contains("New matches", result.HtmlBody);
        Assert.Contains("New likes", result.PlainTextBody);
    }

    [Fact]
    public void RenderDigest_subject_includes_total_count()
    {
        var digest = new DigestModel("u1", new List<NotificationModel>
        {
            MakeNotification("LikeReceived", "{\"likeId\":\"l1\"}"),
            MakeNotification("LikeReceived", "{\"likeId\":\"l2\"}"),
            MakeNotification("MessageReceived", "{\"chatId\":\"c1\",\"preview\":\"hi\"}"),
        });

        var result = _renderer.RenderDigest(digest, "unsub-token-abc");

        Assert.Contains("3", result.Subject);
    }

    [Fact]
    public void RenderSingle_includes_unsubscribe_link_in_footer()
    {
        var notif = MakeNotification("LikeReceived", "{\"likeId\":\"l1\"}");

        var result = _renderer.RenderSingle(notif, "unsub-token-xyz");

        Assert.Contains("unsubscribe", result.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token=unsub-token-xyz", result.HtmlBody);
        Assert.Contains("token=unsub-token-xyz", result.PlainTextBody);
    }

    [Fact]
    public void RenderSingle_with_malformed_payload_does_not_throw()
    {
        var notif = MakeNotification("MessageReceived", "not-valid-json");

        var result = _renderer.RenderSingle(notif, "unsub-token-abc");

        Assert.NotEmpty(result.Subject);
        Assert.NotEmpty(result.HtmlBody);
        Assert.NotEmpty(result.PlainTextBody);
    }
}
