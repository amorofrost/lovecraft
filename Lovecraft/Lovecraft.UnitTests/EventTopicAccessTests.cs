using Lovecraft.Backend.Helpers;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.Enums;
using Xunit;

namespace Lovecraft.UnitTests;

public class EventTopicAccessTests
{
    private static EventDto Ev(EventVisibility v, params string[] attendees) => new()
    {
        Id = "e1",
        Title = "T",
        Visibility = v,
        Attendees = attendees.ToList(),
    };

    [Fact]
    public void Public_TeaserViewer_CanView()
    {
        var ev = Ev(EventVisibility.SecretTeaser);
        var topic = new ForumTopicDto { SectionId = "events", EventTopicVisibility = EventTopicVisibility.Public };
        Assert.True(EventTopicAccess.CanViewEventTopic(ev, topic, "u1", isElevated: false));
    }

    [Fact]
    public void AttendeesOnly_NonAttendee_CannotView()
    {
        var ev = Ev(EventVisibility.Public, "a", "b");
        var topic = new ForumTopicDto { SectionId = "events", EventTopicVisibility = EventTopicVisibility.AttendeesOnly };
        Assert.False(EventTopicAccess.CanViewEventTopic(ev, topic, "u9", isElevated: false));
    }

    [Fact]
    public void AttendeesOnly_Attendee_CanView()
    {
        var ev = Ev(EventVisibility.Public, "a", "b");
        var topic = new ForumTopicDto { SectionId = "events", EventTopicVisibility = EventTopicVisibility.AttendeesOnly };
        Assert.True(EventTopicAccess.CanViewEventTopic(ev, topic, "a", isElevated: false));
    }

    [Fact]
    public void SpecificUsers_ListContains_CanView()
    {
        var ev = Ev(EventVisibility.Public);
        var topic = new ForumTopicDto
        {
            SectionId = "events",
            EventTopicVisibility = EventTopicVisibility.SpecificUsers,
            AllowedUserIds = new List<string> { "x", "y" },
        };
        Assert.True(EventTopicAccess.CanViewEventTopic(ev, topic, "y", isElevated: false));
    }

    [Fact]
    public void SpecificUsers_NotListed_CannotView()
    {
        var ev = Ev(EventVisibility.Public);
        var topic = new ForumTopicDto
        {
            SectionId = "events",
            EventTopicVisibility = EventTopicVisibility.SpecificUsers,
            AllowedUserIds = new List<string> { "x" },
        };
        Assert.False(EventTopicAccess.CanViewEventTopic(ev, topic, "z", isElevated: false));
    }

    [Fact]
    public void Moderator_Always_CanView()
    {
        var ev = Ev(EventVisibility.Public);
        var topic = new ForumTopicDto
        {
            SectionId = "events",
            EventTopicVisibility = EventTopicVisibility.AttendeesOnly,
        };
        Assert.True(EventTopicAccess.CanViewEventTopic(ev, topic, "not-attendee", isElevated: true));
    }
}
