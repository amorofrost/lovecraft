namespace Lovecraft.Common.Enums;

/// <summary>Who may see an event-linked forum topic (section <c>events</c>).</summary>
public enum EventTopicVisibility
{
    /// <summary>Anyone who can see the event discussion area (event summary visibility rules).</summary>
    Public = 0,

    /// <summary>Only users listed as event attendees.</summary>
    AttendeesOnly = 1,

    /// <summary>Only user IDs in the topic&apos;s allow-list.</summary>
    SpecificUsers = 2,
}
