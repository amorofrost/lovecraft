using System;
using System.Linq;
using Lovecraft.NotificationsWorker.Models;
using Lovecraft.NotificationsWorker.Renderers;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class TelegramMessageRendererTests
{
    private readonly TelegramMessageRenderer _renderer = new(NullLogger<TelegramMessageRenderer>.Instance);

    // Extracts and decodes the relative dest path from the "Open in app" web_app button.
    private static string DestOf(InlineKeyboardMarkup keyboard)
    {
        var open = keyboard.InlineKeyboard.SelectMany(row => row).First(b => b.Text.Contains("Open"));
        Assert.Null(open.Url);                    // web_app button carries no plain Url
        Assert.NotNull(open.WebApp);
        var url = open.WebApp!.Url;
        Assert.StartsWith("https://aloeve.club/tg?dest=", url);
        const string marker = "?dest=";
        var encoded = url[(url.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return Uri.UnescapeDataString(encoded);
    }

    [Fact]
    public void LikeReceived_anonymous_omits_actor()
    {
        var notif = new NotificationModel("n1", "u1", "LikeReceived", null,
            "{\"likeId\":\"l1\",\"anonymous\":true}", DateTime.UtcNow);

        var (html, _) = _renderer.Render(notif);

        Assert.Contains("Someone", html);
        Assert.DoesNotContain("<b>Someone</b> liked", html);   // anonymous wording can vary; just check no actor name leak
    }

    [Fact]
    public void MessageReceived_uses_payload_preview()
    {
        var notif = new NotificationModel("n2", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\",\"messageId\":\"m1\",\"preview\":\"hello there\"}", DateTime.UtcNow);

        var (html, _) = _renderer.Render(notif);

        Assert.Contains("hello there", html);
    }

    [Fact]
    public void Open_button_is_a_web_app_button_pointing_at_the_mini_app()
    {
        var notif = new NotificationModel("n3", "u1", "MatchCreated", "actor",
            "{\"matchId\":\"m1\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif);

        Assert.NotNull(keyboard);
        var open = keyboard.InlineKeyboard.SelectMany(row => row).FirstOrDefault(b => b.Text.Contains("Open"));
        Assert.NotNull(open);
        Assert.Null(open!.Url);
        Assert.NotNull(open.WebApp);
        Assert.StartsWith("https://aloeve.club/tg?dest=", open.WebApp!.Url);
    }

    [Fact]
    public void All_notifications_have_mute_callback_button()
    {
        var notif = new NotificationModel("n4", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif);

        var muteButton = keyboard.InlineKeyboard.SelectMany(row => row).FirstOrDefault(b => b.CallbackData?.StartsWith("mute:") == true);
        Assert.NotNull(muteButton);
        Assert.Equal("mute:messageReceived", muteButton!.CallbackData);
    }

    [Fact]
    public void CommunityBroadcast_uses_payload_link()
    {
        var notif = new NotificationModel("n5", "u1", "CommunityBroadcast", null,
            "{\"title\":\"Big news\",\"body\":\"something\",\"link\":\"/aloevera/events/42\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif);

        Assert.Equal("/aloevera/events/42", DestOf(keyboard));
    }

    [Fact]
    public void CommunityBroadcast_disallows_off_domain_absolute_urls()
    {
        var notif = new NotificationModel("n7", "u1", "CommunityBroadcast", null,
            "{\"title\":\"X\",\"body\":\"Y\",\"link\":\"https://evil.example/phish\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif);

        Assert.Equal("/aloevera", DestOf(keyboard));
    }

    [Fact]
    public void MessageReceived_dest_points_to_chat()
    {
        var notif = new NotificationModel("n8", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\",\"preview\":\"hi\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif);

        Assert.Equal("/talks?chat=c1", DestOf(keyboard));
    }

    [Fact]
    public void ForumReply_dest_points_to_topic()
    {
        var notif = new NotificationModel("n9", "u1", "ForumReplyToThread", "actor",
            "{\"topicId\":\"t1\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif);

        Assert.Equal("/talks?topic=t1", DestOf(keyboard));
    }

    [Fact]
    public void EventReminder_dest_points_to_event()
    {
        var notif = new NotificationModel("n10", "u1", "EventReminder", null,
            "{\"eventId\":\"e1\",\"eventTitle\":\"Show\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif);

        Assert.Equal("/aloevera/events/e1", DestOf(keyboard));
    }

    [Fact]
    public void LikeReceived_dest_targets_actor_profile()
    {
        var notif = new NotificationModel("n11", "u1", "LikeReceived", "actor-9",
            "{\"likeId\":\"l1\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif);

        Assert.Equal("/friends?userId=actor-9", DestOf(keyboard));
    }

    [Fact]
    public void RankUp_dest_points_to_settings()
    {
        var notif = new NotificationModel("n12", "u1", "RankUp", null,
            "{\"newRank\":\"aloeCrew\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif);

        Assert.Equal("/settings", DestOf(keyboard));
    }

    [Fact]
    public void Malformed_payload_renders_gracefully()
    {
        var notif = new NotificationModel("n6", "u1", "MessageReceived", "actor",
            "not-valid-json", DateTime.UtcNow);

        var (html, keyboard) = _renderer.Render(notif);

        Assert.NotNull(html);
        Assert.NotEmpty(html);
        Assert.NotNull(keyboard);
    }
}
