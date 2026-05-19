using System.Text.Json;
using System.Web;
using Lovecraft.NotificationsWorker.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types.ReplyMarkups;

namespace Lovecraft.NotificationsWorker.Renderers;

public class TelegramMessageRenderer : ITelegramMessageRenderer
{
    private const string AppBaseUrl = "https://aloeve.club";

    private readonly ILogger<TelegramMessageRenderer> _logger;

    public TelegramMessageRenderer(ILogger<TelegramMessageRenderer> logger)
    {
        _logger = logger;
    }

    public (string Html, InlineKeyboardMarkup Keyboard) Render(NotificationModel notification)
    {
        Dictionary<string, object?> payload;
        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(notification.PayloadJson)
                      ?? new Dictionary<string, object?>();
        }
        catch
        {
            payload = new Dictionary<string, object?>();
            _logger.LogWarning(
                "Notification {NotificationId} has malformed PayloadJson; rendering with empty payload",
                notification.NotificationId);
        }

        string body = notification.Type switch
        {
            "LikeReceived" => IsAnonymous(payload)
                ? "❤️ Someone liked your profile"
                : "❤️ Someone liked your profile",   // actor name lookup deferred to follow-up
            "MatchCreated"          => "💞 You have a new match!",
            "MessageReceived"       => $"💬 New message: {HttpUtility.HtmlEncode(GetString(payload, "preview"))}",
            "ForumReplyToThread"    => "💭 Someone replied in a thread",
            "CommunityBroadcast"    => $"📣 <b>{HttpUtility.HtmlEncode(GetString(payload, "title"))}</b>\n\n{HttpUtility.HtmlEncode(GetString(payload, "body"))}",
            "EventPublished"        => $"📅 New event: <b>{HttpUtility.HtmlEncode(GetString(payload, "eventTitle"))}</b>",
            "EventReminder"         => $"⏰ Event tomorrow: <b>{HttpUtility.HtmlEncode(GetString(payload, "eventTitle"))}</b>",
            "EventInviteReceived"   => $"🎟️ You're invited: <b>{HttpUtility.HtmlEncode(GetString(payload, "eventTitle"))}</b>",
            "RankUp"                => $"🏆 You're now <b>{HttpUtility.HtmlEncode(GetString(payload, "newRank"))}</b>!",
            _                       => "You have a new notification",
        };

        var openUrl  = BuildOpenUrl(notification.Type, notification.ActorId, payload);
        var muteData = $"mute:{ToCamelCase(notification.Type)}";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithUrl("Open in app", openUrl),
                InlineKeyboardButton.WithCallbackData("Mute these", muteData),
            },
        });

        return (body, keyboard);
    }

    private static string BuildOpenUrl(string type, string? actorId, Dictionary<string, object?> payload)
    {
        return type switch
        {
            "LikeReceived" or "MatchCreated" => actorId is not null
                ? $"{AppBaseUrl}/friends?userId={Uri.EscapeDataString(actorId)}"
                : $"{AppBaseUrl}/friends",
            "MessageReceived"    => $"{AppBaseUrl}/talks?chat={Uri.EscapeDataString(GetString(payload, "chatId"))}",
            "ForumReplyToThread" => $"{AppBaseUrl}/talks?topic={Uri.EscapeDataString(GetString(payload, "topicId"))}",
            "EventPublished" or "EventReminder" or "EventInviteReceived" =>
                $"{AppBaseUrl}/aloevera/events/{Uri.EscapeDataString(GetString(payload, "eventId"))}",
            "CommunityBroadcast" => ResolveCommunityBroadcastLink(GetString(payload, "link")),
            "RankUp"             => $"{AppBaseUrl}/settings",
            _                    => AppBaseUrl,
        };
    }

    private static string ResolveCommunityBroadcastLink(string link)
    {
        if (string.IsNullOrEmpty(link))
            return $"{AppBaseUrl}/aloevera";

        if (IsAbsoluteUrl(link))
            return link;

        // Relative path — prepend base URL (path must start with /)
        return link.StartsWith('/') ? $"{AppBaseUrl}{link}" : $"{AppBaseUrl}/{link}";
    }

    private static bool IsAnonymous(Dictionary<string, object?> payload)
    {
        var v = GetString(payload, "anonymous");
        return v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetString(Dictionary<string, object?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var v) || v is null) return string.Empty;
        return v.ToString() ?? string.Empty;
    }

    private static bool IsAbsoluteUrl(string s) =>
        Uri.TryCreate(s, UriKind.Absolute, out _);

    private static string ToCamelCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal)) return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }
}
