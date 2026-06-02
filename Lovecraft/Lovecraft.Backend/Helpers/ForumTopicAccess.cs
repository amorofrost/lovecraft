using System.Security.Claims;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Helpers;

/// <summary>
/// Single source of truth for "may this caller view this forum topic", combining event-topic
/// visibility, MinRank, and NoviceVisible. Shared by ForumController (REST) and ChatHub (SignalR
/// JoinTopic) so the two authorization paths can't drift.
/// </summary>
public interface IForumTopicAccess
{
    /// <summary>Event-topic visibility check only (true for non-event sections). The controller uses
    /// this and layers its own MinRank / NoviceVisible checks with distinct REST error codes.</summary>
    Task<bool> CanViewEventTopicContentAsync(ClaimsPrincipal user, ForumTopicDto topic);

    /// <summary>Full combined check (event-topic visibility + MinRank + NoviceVisible). The hub uses
    /// this — it only needs a boolean.</summary>
    Task<bool> CanViewTopicAsync(ClaimsPrincipal user, string topicId);
}

public sealed class ForumTopicAccess : IForumTopicAccess
{
    private readonly IForumService _forum;
    private readonly IEventService _events;
    private readonly IUserService _users;

    public ForumTopicAccess(IForumService forum, IEventService events, IUserService users)
    {
        _forum = forum;
        _events = events;
        _users = users;
    }

    public async Task<bool> CanViewEventTopicContentAsync(ClaimsPrincipal user, ForumTopicDto topic)
    {
        if (!topic.SectionId.Equals("events", StringComparison.OrdinalIgnoreCase))
            return true;

        var eventId = ResolveEventIdFromTopic(topic);
        if (string.IsNullOrEmpty(eventId)) return false;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return false;

        var staff = user.FindFirst("staffRole")?.Value ?? "none";
        var isElevated = staff is "moderator" or "admin";

        var ev = await _events.GetEventByIdAdminAsync(eventId);
        if (ev is null) return false;

        return EventTopicAccess.CanViewEventTopic(ev, topic, userId, isElevated);
    }

    public async Task<bool> CanViewTopicAsync(ClaimsPrincipal user, string topicId)
    {
        var topic = await _forum.GetTopicByIdAsync(topicId);
        if (topic is null) return false;

        if (!await CanViewEventTopicContentAsync(user, topic)) return false;

        if (!await PermissionGuard.MeetsAsync(user, _users, topic.MinRank)) return false;

        if (!topic.NoviceVisible)
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var rank = (string.IsNullOrEmpty(userId) ? null : (await _users.GetUserByIdAsync(userId))?.Rank)
                       ?? UserRank.Novice;
            if (rank == UserRank.Novice) return false;
        }

        return true;
    }

    /// <summary>Resolve the owning event id from an event-section topic (moved here from
    /// ForumController so both paths share one implementation).</summary>
    public static string? ResolveEventIdFromTopic(ForumTopicDto t)
    {
        if (!string.IsNullOrEmpty(t.EventId))
            return t.EventId;
        if (t.Id.StartsWith("evt-", StringComparison.Ordinal) && t.Id.Length > 4)
            return t.Id.Substring(4);
        if (t.Id.StartsWith("event-topic-", StringComparison.Ordinal) && t.Id.Length > "event-topic-".Length)
            return t.Id["event-topic-".Length..];
        return null;
    }
}
