using System;
using System.Linq;
using Lovecraft.Common.Enums;
using Lovecraft.NotificationsWorker.Models;
using Lovecraft.NotificationsWorker.Renderers;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class TelegramMessageRendererTests
{
    private readonly TelegramMessageRenderer _renderer = new(NullLogger<TelegramMessageRenderer>.Instance);

    // Locates the "Open in app" web_app button structurally (its label is localized, so we
    // cannot match on text) and decodes its relative dest path.
    private static string DestOf(InlineKeyboardMarkup keyboard)
    {
        var open = keyboard.InlineKeyboard.SelectMany(row => row).First(b => b.WebApp is not null);
        Assert.Null(open.Url);
        var url = open.WebApp!.Url;
        Assert.StartsWith("https://aloeve.club/tg?dest=", url);
        const string marker = "?dest=";
        var encoded = url[(url.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return Uri.UnescapeDataString(encoded);
    }

    private static Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton OpenButton(InlineKeyboardMarkup keyboard) =>
        keyboard.InlineKeyboard.SelectMany(row => row).First(b => b.WebApp is not null);

    private static Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton MuteButton(InlineKeyboardMarkup keyboard) =>
        keyboard.InlineKeyboard.SelectMany(row => row).First(b => b.CallbackData?.StartsWith("mute:") == true);

    // ---- Localized bodies ----

    [Fact]
    public void MessageReceived_body_is_russian_for_ru()
    {
        var notif = new NotificationModel("n2", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\",\"preview\":\"hello there\"}", DateTime.UtcNow);

        var (html, _) = _renderer.Render(notif, Language.Ru);

        Assert.Contains("Новое сообщение", html);
        Assert.Contains("hello there", html);   // preview content passes through untranslated
    }

    [Fact]
    public void MessageReceived_body_is_english_for_en()
    {
        var notif = new NotificationModel("n2", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\",\"preview\":\"hello there\"}", DateTime.UtcNow);

        var (html, _) = _renderer.Render(notif, Language.En);

        Assert.Contains("New message", html);
        Assert.Contains("hello there", html);
    }

    [Fact]
    public void MatchCreated_body_localized()
    {
        var notif = new NotificationModel("n1", "u1", "MatchCreated", "actor", "{}", DateTime.UtcNow);

        Assert.Contains("новый мэтч", _renderer.Render(notif, Language.Ru).Html);
        Assert.Contains("new match", _renderer.Render(notif, Language.En).Html);
    }

    [Fact]
    public void EventReminder_body_localized_title_passthrough()
    {
        var notif = new NotificationModel("n10", "u1", "EventReminder", null,
            "{\"eventId\":\"e1\",\"eventTitle\":\"Show\"}", DateTime.UtcNow);

        var ru = _renderer.Render(notif, Language.Ru).Html;
        Assert.Contains("Событие завтра", ru);
        Assert.Contains("Show", ru);            // event title passes through
        Assert.Contains("Event tomorrow", _renderer.Render(notif, Language.En).Html);
    }

    [Fact]
    public void RankUp_uses_localized_rank_name()
    {
        var notif = new NotificationModel("n12", "u1", "RankUp", null,
            "{\"newRank\":\"aloeCrew\"}", DateTime.UtcNow);

        Assert.Contains("Команда AloeVera", _renderer.Render(notif, Language.Ru).Html);
        Assert.Contains("Aloe Crew", _renderer.Render(notif, Language.En).Html);
    }

    [Fact]
    public void Default_notification_localized()
    {
        var notif = new NotificationModel("nz", "u1", "SomethingUnknown", null, "{}", DateTime.UtcNow);

        Assert.Contains("новое уведомление", _renderer.Render(notif, Language.Ru).Html);
        Assert.Contains("new notification", _renderer.Render(notif, Language.En).Html);
    }

    // ---- Buttons ----

    [Fact]
    public void Open_button_is_a_web_app_button_pointing_at_the_mini_app()
    {
        var notif = new NotificationModel("n3", "u1", "MatchCreated", "actor",
            "{\"matchId\":\"m1\"}", DateTime.UtcNow);

        var (_, keyboard) = _renderer.Render(notif, Language.Ru);

        Assert.NotNull(keyboard);
        var open = OpenButton(keyboard);
        Assert.Null(open.Url);
        Assert.StartsWith("https://aloeve.club/tg?dest=", open.WebApp!.Url);
    }

    [Fact]
    public void Open_button_label_is_localized()
    {
        var notif = new NotificationModel("n3", "u1", "MatchCreated", "actor", "{}", DateTime.UtcNow);

        Assert.Equal("Открыть в приложении", OpenButton(_renderer.Render(notif, Language.Ru).Keyboard).Text);
        Assert.Equal("Open in app", OpenButton(_renderer.Render(notif, Language.En).Keyboard).Text);
    }

    [Fact]
    public void Mute_button_data_stable_label_localized()
    {
        var notif = new NotificationModel("n4", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\"}", DateTime.UtcNow);

        var muteRu = MuteButton(_renderer.Render(notif, Language.Ru).Keyboard);
        var muteEn = MuteButton(_renderer.Render(notif, Language.En).Keyboard);
        Assert.Equal("mute:messageReceived", muteRu.CallbackData);   // data unchanged
        Assert.Equal("mute:messageReceived", muteEn.CallbackData);
        Assert.Equal("Отключить эти", muteRu.Text);
        Assert.Equal("Mute these", muteEn.Text);
    }

    // ---- dest paths (unchanged behavior; language-agnostic) ----

    [Fact]
    public void CommunityBroadcast_uses_payload_link()
    {
        var notif = new NotificationModel("n5", "u1", "CommunityBroadcast", null,
            "{\"title\":\"Big news\",\"body\":\"something\",\"link\":\"/aloevera/events/42\"}", DateTime.UtcNow);

        Assert.Equal("/aloevera/events/42", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void CommunityBroadcast_body_passes_through_untranslated()
    {
        var notif = new NotificationModel("n5", "u1", "CommunityBroadcast", null,
            "{\"title\":\"Big news\",\"body\":\"something\"}", DateTime.UtcNow);

        var html = _renderer.Render(notif, Language.Ru).Html;
        Assert.Contains("Big news", html);
        Assert.Contains("something", html);
    }

    [Fact]
    public void CommunityBroadcast_disallows_off_domain_absolute_urls()
    {
        var notif = new NotificationModel("n7", "u1", "CommunityBroadcast", null,
            "{\"title\":\"X\",\"body\":\"Y\",\"link\":\"https://evil.example/phish\"}", DateTime.UtcNow);

        Assert.Equal("/aloevera", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void MessageReceived_dest_points_to_chat()
    {
        var notif = new NotificationModel("n8", "u1", "MessageReceived", "actor",
            "{\"chatId\":\"c1\",\"preview\":\"hi\"}", DateTime.UtcNow);

        Assert.Equal("/talks?chat=c1", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void ForumReply_dest_points_to_topic()
    {
        var notif = new NotificationModel("n9", "u1", "ForumReplyToThread", "actor",
            "{\"topicId\":\"t1\"}", DateTime.UtcNow);

        Assert.Equal("/talks?topic=t1", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void EventReminder_dest_points_to_event()
    {
        var notif = new NotificationModel("n10", "u1", "EventReminder", null,
            "{\"eventId\":\"e1\",\"eventTitle\":\"Show\"}", DateTime.UtcNow);

        Assert.Equal("/aloevera/events/e1", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void LikeReceived_dest_targets_actor_profile()
    {
        var notif = new NotificationModel("n11", "u1", "LikeReceived", "actor-9",
            "{\"likeId\":\"l1\"}", DateTime.UtcNow);

        Assert.Equal("/friends?userId=actor-9", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void RankUp_dest_points_to_settings()
    {
        var notif = new NotificationModel("n12", "u1", "RankUp", null,
            "{\"newRank\":\"aloeCrew\"}", DateTime.UtcNow);

        Assert.Equal("/settings", DestOf(_renderer.Render(notif, Language.Ru).Keyboard));
    }

    [Fact]
    public void Malformed_payload_renders_gracefully()
    {
        var notif = new NotificationModel("n6", "u1", "MessageReceived", "actor",
            "not-valid-json", DateTime.UtcNow);

        var (html, keyboard) = _renderer.Render(notif, Language.Ru);

        Assert.NotNull(html);
        Assert.NotEmpty(html);
        Assert.NotNull(keyboard);
    }
}
