using System.Security.Claims;
using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class ForumTopicAccessTests
{
    private static ClaimsPrincipal User(string id, string staffRole = "none") =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim("staffRole", staffRole),
        }, "test"));

    private static ForumTopicDto EventTopic(EventTopicVisibility vis, string id = "event-topic-e1") => new()
    {
        Id = id, SectionId = "events", EventId = "e1",
        EventTopicVisibility = vis, MinRank = "novice", NoviceVisible = true,
    };

    private static (ForumTopicAccess access, Mock<IForumService> forum, Mock<IEventService> events, Mock<IUserService> users) Build()
    {
        var forum = new Mock<IForumService>();
        var events = new Mock<IEventService>();
        var users = new Mock<IUserService>();
        // Default: any user resolves to a high rank (so MinRank/NoviceVisible don't accidentally gate).
        users.Setup(u => u.GetUserByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new UserDto { Id = id, Rank = UserRank.AloeCrew });
        return (new ForumTopicAccess(forum.Object, events.Object, users.Object), forum, events, users);
    }

    [Fact]
    public async Task AttendeesOnly_NonAttendee_CannotViewTopic()
    {
        var (access, forum, events, _) = Build();
        var topic = EventTopic(EventTopicVisibility.AttendeesOnly);
        forum.Setup(f => f.GetTopicByIdAsync(topic.Id)).ReturnsAsync(topic);
        events.Setup(e => e.GetEventByIdAdminAsync("e1"))
            .ReturnsAsync(new EventDto { Id = "e1", Attendees = new List<string> { "a", "b" } });

        Assert.False(await access.CanViewTopicAsync(User("intruder"), topic.Id));
    }

    [Fact]
    public async Task AttendeesOnly_Attendee_CanViewTopic()
    {
        var (access, forum, events, _) = Build();
        var topic = EventTopic(EventTopicVisibility.AttendeesOnly);
        forum.Setup(f => f.GetTopicByIdAsync(topic.Id)).ReturnsAsync(topic);
        events.Setup(e => e.GetEventByIdAdminAsync("e1"))
            .ReturnsAsync(new EventDto { Id = "e1", Attendees = new List<string> { "a", "b" } });

        Assert.True(await access.CanViewTopicAsync(User("a"), topic.Id));
    }

    [Fact]
    public async Task AttendeesOnly_Moderator_CanViewTopic()
    {
        var (access, forum, events, _) = Build();
        var topic = EventTopic(EventTopicVisibility.AttendeesOnly);
        forum.Setup(f => f.GetTopicByIdAsync(topic.Id)).ReturnsAsync(topic);
        events.Setup(e => e.GetEventByIdAdminAsync("e1"))
            .ReturnsAsync(new EventDto { Id = "e1", Attendees = new List<string>() });

        Assert.True(await access.CanViewTopicAsync(User("mod", "moderator"), topic.Id));
    }

    [Fact]
    public async Task NoviceHiddenTopic_NoviceCannotView()
    {
        var (access, forum, _, users) = Build();
        var topic = new ForumTopicDto { Id = "t1", SectionId = "general", MinRank = "novice", NoviceVisible = false };
        forum.Setup(f => f.GetTopicByIdAsync("t1")).ReturnsAsync(topic);
        users.Setup(u => u.GetUserByIdAsync("nov")).ReturnsAsync(new UserDto { Id = "nov", Rank = UserRank.Novice });

        Assert.False(await access.CanViewTopicAsync(User("nov"), "t1"));
    }

    [Fact]
    public async Task PublicGeneralTopic_AnyUserCanView()
    {
        var (access, forum, _, _) = Build();
        var topic = new ForumTopicDto { Id = "t2", SectionId = "general", MinRank = "novice", NoviceVisible = true };
        forum.Setup(f => f.GetTopicByIdAsync("t2")).ReturnsAsync(topic);

        Assert.True(await access.CanViewTopicAsync(User("anyone"), "t2"));
    }

    [Fact]
    public async Task MissingTopic_CannotView()
    {
        var (access, forum, _, _) = Build();
        forum.Setup(f => f.GetTopicByIdAsync("ghost")).ReturnsAsync((ForumTopicDto?)null);
        Assert.False(await access.CanViewTopicAsync(User("u"), "ghost"));
    }

    [Fact]
    public async Task SpecificUsers_OnlyListedCanView()
    {
        var (access, forum, events, _) = Build();
        var topic = EventTopic(EventTopicVisibility.SpecificUsers);
        topic.AllowedUserIds = new List<string> { "x" };
        forum.Setup(f => f.GetTopicByIdAsync(topic.Id)).ReturnsAsync(topic);
        events.Setup(e => e.GetEventByIdAdminAsync("e1")).ReturnsAsync(new EventDto { Id = "e1" });

        Assert.True(await access.CanViewTopicAsync(User("x"), topic.Id));
        Assert.False(await access.CanViewTopicAsync(User("z"), topic.Id));
    }

    [Fact]
    public async Task RankGatedTopic_BelowRankUser_CannotView()
    {
        var (access, forum, _, users) = Build();
        var topic = new ForumTopicDto { Id = "t3", SectionId = "general", MinRank = "aloeCrew", NoviceVisible = true };
        forum.Setup(f => f.GetTopicByIdAsync("t3")).ReturnsAsync(topic);
        users.Setup(u => u.GetUserByIdAsync("low")).ReturnsAsync(new UserDto { Id = "low", Rank = UserRank.Novice });

        Assert.False(await access.CanViewTopicAsync(User("low"), "t3"));
    }
}
