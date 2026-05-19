using System.Text;
using System.Text.Json;
using System.Web;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.NotificationsWorker.Models;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Renderers;

public class EmailDigestRenderer : IEmailDigestRenderer
{
    private readonly string _unsubscribeBaseUrl;
    private readonly string _appBaseUrl;
    private readonly ILogger<EmailDigestRenderer> _logger;

    public EmailDigestRenderer(string unsubscribeBaseUrl, string appBaseUrl, ILogger<EmailDigestRenderer> logger)
    {
        _unsubscribeBaseUrl = unsubscribeBaseUrl.TrimEnd('/');
        _appBaseUrl = appBaseUrl.TrimEnd('/');
        _logger = logger;
    }

    public EmailRenderResultDto RenderSingle(NotificationModel notification, string unsubscribeToken)
    {
        var (subject, sections) = BuildSections(new List<NotificationModel> { notification });
        return Build(subject, sections, unsubscribeToken);
    }

    public EmailRenderResultDto RenderDigest(DigestModel digest, string unsubscribeToken)
    {
        var (subject, sections) = BuildSections(digest.Members);
        return Build(subject, sections, unsubscribeToken);
    }

    private (string Subject, List<EmailDigestSectionDto> Sections) BuildSections(IReadOnlyList<NotificationModel> notifications)
    {
        // Group by type
        var byType = notifications
            .GroupBy(n => n.Type)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sections = new List<EmailDigestSectionDto>();
        foreach (var (type, items) in byType)
        {
            var section = new EmailDigestSectionDto
            {
                Type = type,
                Header = SectionHeader(type, items.Count),
                Items = items.Select(BuildItem).ToList(),
            };
            sections.Add(section);
        }

        var subject = BuildSubject(notifications.Count, byType);
        return (subject, sections);
    }

    private static string SectionHeader(string type, int count) => type switch
    {
        "LikeReceived"        => $"New likes ({count})",
        "MatchCreated"        => $"New matches ({count})",
        "MessageReceived"     => $"New messages ({count})",
        "ForumReplyToThread"  => $"New replies ({count})",
        "CommunityBroadcast"  => $"Community updates ({count})",
        "EventPublished"      => $"New events ({count})",
        "EventReminder"       => $"Event reminders ({count})",
        "EventInviteReceived" => $"Event invites ({count})",
        "RankUp"              => $"Rank up ({count})",
        _ => $"{type} ({count})",
    };

    private EmailDigestItemDto BuildItem(NotificationModel n)
    {
        Dictionary<string, object?> payload;
        try { payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(n.PayloadJson) ?? new(); }
        catch { payload = new(); _logger.LogWarning("Malformed payload in notification {Id}", n.NotificationId); }

        var text = n.Type switch
        {
            "LikeReceived"        => IsAnonymous(payload) ? "Someone liked your profile" : "Someone liked your profile",
            "MatchCreated"        => "You have a new match!",
            "MessageReceived"     => $"{GetString(payload, "preview")}",
            "ForumReplyToThread"  => "Someone replied in a thread",
            "CommunityBroadcast"  => $"{GetString(payload, "title")} — {GetString(payload, "body")}",
            "EventPublished"      => GetString(payload, "eventTitle", "New event"),
            "EventReminder"       => GetString(payload, "eventTitle", "Event tomorrow"),
            "EventInviteReceived" => GetString(payload, "eventTitle", "You're invited"),
            "RankUp"              => $"You're now {GetString(payload, "newRank")}",
            _ => $"Notification {n.NotificationId}",
        };

        var url = _appBaseUrl + (n.Type switch
        {
            "LikeReceived" or "MatchCreated" => n.ActorId is not null ? $"/friends?userId={Uri.EscapeDataString(n.ActorId)}" : "/friends",
            "MessageReceived"     => $"/talks?chat={Uri.EscapeDataString(GetString(payload, "chatId"))}",
            "ForumReplyToThread"  => $"/talks?topic={Uri.EscapeDataString(GetString(payload, "topicId"))}",
            "EventPublished" or "EventReminder" or "EventInviteReceived" =>
                $"/aloevera/events/{Uri.EscapeDataString(GetString(payload, "eventId"))}",
            "CommunityBroadcast"  => ResolveBroadcastPath(GetString(payload, "link")),
            "RankUp"              => "/settings",
            _ => "/notifications",
        });

        return new EmailDigestItemDto { Text = text, Url = url };
    }

    private static string ResolveBroadcastPath(string link)
    {
        if (string.IsNullOrEmpty(link)) return "/aloevera";
        if (Uri.TryCreate(link, UriKind.Absolute, out var abs))
        {
            if (abs.Scheme == Uri.UriSchemeHttps
                && (abs.Host.Equals("aloeve.club", StringComparison.OrdinalIgnoreCase)
                    || abs.Host.Equals("www.aloeve.club", StringComparison.OrdinalIgnoreCase)))
                return abs.PathAndQuery;
            return "/aloevera";
        }
        return link.StartsWith('/') ? link : "/" + link;
    }

    private static string BuildSubject(int total, Dictionary<string, List<NotificationModel>> byType)
    {
        if (total == 1) return "You have a new notification on AloeVera";
        var summary = string.Join(", ", byType.Select(kv => $"{kv.Value.Count} {ShortName(kv.Key)}"));
        return $"{total} updates: {summary}";
    }

    private static string ShortName(string type) => type switch
    {
        "LikeReceived" => "likes",
        "MatchCreated" => "matches",
        "MessageReceived" => "messages",
        "ForumReplyToThread" => "replies",
        "CommunityBroadcast" => "community updates",
        "EventPublished" => "new events",
        "EventReminder" => "event reminders",
        "EventInviteReceived" => "event invites",
        "RankUp" => "rank ups",
        _ => "notifications",
    };

    private EmailRenderResultDto Build(string subject, List<EmailDigestSectionDto> sections, string unsubscribeToken)
    {
        var unsubscribeUrl = $"{_unsubscribeBaseUrl}/api/v1/notifications/unsubscribe?token={Uri.EscapeDataString(unsubscribeToken)}";
        var settingsUrl = $"{_appBaseUrl}/settings";

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html><html><body style=\"font-family: sans-serif; max-width: 600px; margin: 0 auto; padding: 24px;\">");
        html.AppendLine("<h2 style=\"color: #c84f00; margin-bottom: 16px;\">AloeVera</h2>");
        foreach (var section in sections)
        {
            html.AppendLine($"<h3 style=\"margin-top: 24px; margin-bottom: 8px;\">{HttpUtility.HtmlEncode(section.Header)}</h3>");
            html.AppendLine("<ul style=\"padding-left: 20px;\">");
            foreach (var item in section.Items)
                html.AppendLine($"<li style=\"margin-bottom: 8px;\">{HttpUtility.HtmlEncode(item.Text)} <a href=\"{HttpUtility.HtmlEncode(item.Url)}\">Open</a></li>");
            html.AppendLine("</ul>");
        }
        html.AppendLine("<hr style=\"border: none; border-top: 1px solid #ddd; margin-top: 32px;\">");
        html.AppendLine($"<p style=\"color: #888; font-size: 12px;\">");
        html.AppendLine($"<a href=\"{HttpUtility.HtmlEncode(settingsUrl)}\" style=\"color: #888;\">Manage notifications</a> &middot; ");
        html.AppendLine($"<a href=\"{HttpUtility.HtmlEncode(unsubscribeUrl)}\" style=\"color: #888;\">Unsubscribe from email digests</a>");
        html.AppendLine("</p></body></html>");

        var plain = new StringBuilder();
        plain.AppendLine("AloeVera Harmony Meet");
        plain.AppendLine();
        foreach (var section in sections)
        {
            plain.AppendLine(section.Header);
            foreach (var item in section.Items)
                plain.AppendLine($"  - {item.Text} ({item.Url})");
            plain.AppendLine();
        }
        plain.AppendLine("---");
        plain.AppendLine($"Manage notifications: {settingsUrl}");
        plain.AppendLine($"Unsubscribe: {unsubscribeUrl}");

        return new EmailRenderResultDto
        {
            Subject = subject,
            HtmlBody = html.ToString(),
            PlainTextBody = plain.ToString(),
        };
    }

    private static bool IsAnonymous(Dictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("anonymous", out var v) || v is null) return false;
        var s = v.ToString();
        return s == "True" || s == "true";
    }

    private static string GetString(Dictionary<string, object?> payload, string key, string fallback = "")
    {
        if (!payload.TryGetValue(key, out var v) || v is null) return fallback;
        return v.ToString() ?? fallback;
    }
}
