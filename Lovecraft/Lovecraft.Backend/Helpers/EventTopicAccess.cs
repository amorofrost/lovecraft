using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Helpers;

/// <summary>Per-topic visibility for event forum threads (section <c>events</c>).</summary>
public static class EventTopicAccess
{
    public static bool CanViewEventTopic(EventDto ev, ForumTopicDto topic, string userId, bool isElevated)
    {
        if (isElevated)
            return true;

        return topic.EventTopicVisibility switch
        {
            EventTopicVisibility.Public =>
                EventForumAccess.CanViewEventDiscussionSummary(ev, userId, false),
            EventTopicVisibility.AttendeesOnly =>
                ev.Attendees.Contains(userId),
            EventTopicVisibility.SpecificUsers =>
                topic.AllowedUserIds.Count > 0 && topic.AllowedUserIds.Contains(userId),
            _ => EventForumAccess.CanViewEventDiscussionSummary(ev, userId, false),
        };
    }
}
