using Lovecraft.NotificationsWorker.Models;
using Lovecraft.NotificationsWorker.Renderers;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class TelegramMessageRendererTests
{
    private readonly TelegramMessageRenderer _renderer = new(NullLogger<TelegramMessageRenderer>.Instance);

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
    public void All_notifications_have_open_in_app_button_with_aloeve_url()
    {
        var notif = new NotificationModel("n3", "u1", "MatchCreated", "actor",
            "{\"matchId\":\"m1\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif);

        Assert.NotNull(keyboard);
        var buttons = keyboard.InlineKeyboard.SelectMany(row => row).ToList();
        var openButton = buttons.FirstOrDefault(b => b.Text.Contains("Open"));
        Assert.NotNull(openButton);
        Assert.StartsWith("https://aloeve.club/", openButton!.Url);
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

        var openButton = keyboard.InlineKeyboard.SelectMany(row => row).First(b => b.Text.Contains("Open"));
        Assert.Equal("https://aloeve.club/aloevera/events/42", openButton.Url);
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
