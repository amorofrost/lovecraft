using System;
using System.Text.Json;
using System.Web;
using Lovecraft.NotificationsWorker.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Lovecraft.NotificationsWorker.Renderers;

public class TelegramMessageRenderer : ITelegramMessageRenderer
{
    private const string MiniAppUrl = "https://aloeve.club/tg";

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

        var destPath  = BuildDestPath(notification.Type, notification.ActorId, payload);
        var webAppUrl = $"{MiniAppUrl}?dest={Uri.EscapeDataString(destPath)}";
        var muteData  = $"mute:{ToCamelCase(notification.Type)}";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithWebApp("Open in app", new WebAppInfo { Url = webAppUrl }),
                InlineKeyboardButton.WithCallbackData("Mute these", muteData),
            },
        });

        return (body, keyboard);
    }

    private static string BuildDestPath(string type, string? actorId, Dictionary<string, object?> payload)
    {
        return type switch
        {
            "LikeReceived" or "MatchCreated" => actorId is not null
                ? $"/friends?userId={Uri.EscapeDataString(actorId)}"
                : "/friends",
            "MessageReceived"    => $"/talks?chat={Uri.EscapeDataString(GetString(payload, "chatId"))}",
            "ForumReplyToThread" => $"/talks?topic={Uri.EscapeDataString(GetString(payload, "topicId"))}",
            "EventPublished" or "EventReminder" or "EventInviteReceived" =>
                $"/aloevera/events/{Uri.EscapeDataString(GetString(payload, "eventId"))}",
            "CommunityBroadcast" => ResolveCommunityBroadcastPath(GetString(payload, "link")),
            "RankUp"             => "/settings",
            _                    => "/",
        };
    }

    private static string ResolveCommunityBroadcastPath(string link)
    {
        if (string.IsNullOrEmpty(link))
            return "/aloevera";

        // A rooted path is already an in-app relative path. This MUST be checked before
        // Uri.TryCreate: on Unix a leading-'/' string parses as an absolute file:// URI
        // (Uri.TryCreate(..., Absolute) returns true), which would otherwise send every
        // relative link down the "disallowed absolute" fallback. Reject protocol-relative
        // '//host' (open-redirect surface) → safe default.
        if (link.StartsWith('/'))
            return link.StartsWith("//") ? "/aloevera" : link;

        if (Uri.TryCreate(link, UriKind.Absolute, out var absolute))
        {
            // Only allow absolute URLs pointing to the app's own domain; use just the path+query.
            if (absolute.Scheme == Uri.UriSchemeHttps &&
                (absolute.Host.Equals("aloeve.club", StringComparison.OrdinalIgnoreCase) ||
                 absolute.Host.Equals("www.aloeve.club", StringComparison.OrdinalIgnoreCase)))
            {
                return absolute.PathAndQuery;
            }
            // Disallowed absolute URL (off-domain or non-HTTPS) — safe default.
            return "/aloevera";
        }

        // Non-rooted, non-absolute (e.g. "aloevera") — treat as a path.
        return $"/{link}";
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

    private static string ToCamelCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal)) return pascal;
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }
}
