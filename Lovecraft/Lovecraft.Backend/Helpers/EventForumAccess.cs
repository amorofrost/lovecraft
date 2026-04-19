using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Helpers;

/// <summary>Visibility rules for event-linked forum UI (sections vs topics/replies).</summary>
public static class EventForumAccess
{
    /// <summary>Talks → event discussion cards: public, teaser, or attended hidden; staff see all.</summary>
    public static bool CanViewEventDiscussionSummary(EventDto e, string userId, bool isElevated)
    {
        if (isElevated) return true;
        if (e.Visibility == EventVisibility.Public) return true;
        if (e.Visibility == EventVisibility.SecretTeaser) return true;
        if (e.Visibility == EventVisibility.SecretHidden && e.Attendees.Contains(userId)) return true;
        return false;
    }

    /// <summary>Topics and replies: public events, or anyone on the attendee list; not teaser-only viewers.</summary>
    public static bool CanViewTopicsAndReplies(EventDto e, string userId, bool isElevated)
    {
        if (isElevated) return true;
        if (e.Visibility == EventVisibility.Public) return true;
        if (e.Attendees.Contains(userId)) return true;
        return false;
    }
}
