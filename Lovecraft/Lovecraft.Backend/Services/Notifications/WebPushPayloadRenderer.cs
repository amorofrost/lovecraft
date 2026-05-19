using System.Text.Json;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Services.Notifications;

public class WebPushPayloadRenderer : IWebPushPayloadRenderer
{
    private readonly ILogger<WebPushPayloadRenderer> _logger;

    public WebPushPayloadRenderer(ILogger<WebPushPayloadRenderer> logger)
    {
        _logger = logger;
    }

    public WebPushNotificationDto Render(NotificationDto notification)
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
            _logger.LogWarning("Notification {NotificationId} has malformed PayloadJson; rendering with empty payload",
                notification.Id);
        }

        return notification.Type switch
        {
            NotificationType.LikeReceived => new WebPushNotificationDto
            {
                Title = "New like",
                Body = "Someone liked your profile",
                Url = BuildFriendsUrl(notification.ActorId),
            },
            NotificationType.MatchCreated => new WebPushNotificationDto
            {
                Title = "New match!",
                Body = "You have a new match",
                Url = BuildFriendsUrl(notification.ActorId),
            },
            NotificationType.MessageReceived => new WebPushNotificationDto
            {
                Title = "New message",
                Body = GetString(payload, "preview"),
                Url = $"/talks?chat={Uri.EscapeDataString(GetString(payload, "chatId"))}",
            },
            NotificationType.ForumReplyToThread => new WebPushNotificationDto
            {
                Title = "New reply",
                Body = "Someone replied in a thread",
                Url = $"/talks?topic={Uri.EscapeDataString(GetString(payload, "topicId"))}",
            },
            NotificationType.CommunityBroadcast => new WebPushNotificationDto
            {
                Title = GetString(payload, "title", fallback: "Community update"),
                Body = GetString(payload, "body"),
                Url = ResolveCommunityBroadcastUrl(GetString(payload, "link")),
            },
            NotificationType.EventPublished => new WebPushNotificationDto
            {
                Title = "New event",
                Body = GetString(payload, "eventTitle"),
                Url = $"/aloevera/events/{Uri.EscapeDataString(GetString(payload, "eventId"))}",
            },
            NotificationType.EventReminder => new WebPushNotificationDto
            {
                Title = "Event tomorrow",
                Body = GetString(payload, "eventTitle"),
                Url = $"/aloevera/events/{Uri.EscapeDataString(GetString(payload, "eventId"))}",
            },
            NotificationType.EventInviteReceived => new WebPushNotificationDto
            {
                Title = "You're invited",
                Body = GetString(payload, "eventTitle"),
                Url = $"/aloevera/events/{Uri.EscapeDataString(GetString(payload, "eventId"))}",
            },
            NotificationType.RankUp => new WebPushNotificationDto
            {
                Title = "Rank up!",
                Body = $"You're now {GetString(payload, "newRank")}",
                Url = "/settings",
            },
            _ => new WebPushNotificationDto
            {
                Title = "New notification",
                Body = "You have a new notification",
                Url = "/notifications",
            },
        };
    }

    private static string BuildFriendsUrl(string? actorId)
        => actorId is not null ? $"/friends?userId={Uri.EscapeDataString(actorId)}" : "/friends";

    private static string ResolveCommunityBroadcastUrl(string link)
    {
        if (string.IsNullOrEmpty(link)) return "/aloevera";

        if (Uri.TryCreate(link, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme == Uri.UriSchemeHttps
                && (absolute.Host.Equals("aloeve.club", StringComparison.OrdinalIgnoreCase)
                    || absolute.Host.Equals("www.aloeve.club", StringComparison.OrdinalIgnoreCase)))
            {
                return absolute.PathAndQuery;   // strip scheme + host, return path-relative URL
            }
            return "/aloevera";
        }

        return link.StartsWith('/') ? link : "/" + link;
    }

    private static string GetString(Dictionary<string, object?> payload, string key, string fallback = "")
    {
        if (!payload.TryGetValue(key, out var v) || v is null) return fallback;
        return v.ToString() ?? fallback;
    }
}
