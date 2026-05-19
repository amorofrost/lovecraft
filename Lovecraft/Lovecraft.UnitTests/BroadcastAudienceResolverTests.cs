using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class BroadcastAudienceResolverTests
{
    private static UserDto User(string id, UserRank rank = UserRank.Novice, StaffRole staff = StaffRole.None) =>
        new() { Id = id, Name = id, Rank = rank, StaffRole = staff };

    private static (BroadcastAudienceResolver Resolver, Mock<IUserService> Users, Mock<IEventService> Events) Build(
        IEnumerable<UserDto>? users = null,
        IEnumerable<EventAttendeeAdminDto>? attendees = null)
    {
        var u = new Mock<IUserService>();
        u.Setup(s => s.GetUsersAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync((users ?? Enumerable.Empty<UserDto>()).ToList());
        var e = new Mock<IEventService>();
        e.Setup(s => s.GetEventAttendeesAsync(It.IsAny<string>()))
            .ReturnsAsync((attendees ?? Enumerable.Empty<EventAttendeeAdminDto>()).ToList());
        return (new BroadcastAudienceResolver(u.Object, e.Object), u, e);
    }

    [Fact]
    public async Task Resolve_all_returns_every_user_id()
    {
        var (resolver, _, _) = Build(new[]
        {
            User("u1"), User("u2"), User("u3"),
        });

        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("all", null), CancellationToken.None);

        Assert.Equal(3, ids.Count);
        Assert.Contains("u1", ids);
        Assert.Contains("u2", ids);
        Assert.Contains("u3", ids);
    }

    [Fact]
    public async Task Resolve_all_returns_empty_when_no_users()
    {
        var (resolver, _, _) = Build(Array.Empty<UserDto>());
        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("all", null), CancellationToken.None);
        Assert.Empty(ids);
    }

    [Fact]
    public async Task Resolve_attendingEvent_returns_only_event_attendees()
    {
        var (resolver, _, eventsMock) = Build(
            users: new[] { User("a"), User("b"), User("c") },
            attendees: new[]
            {
                new EventAttendeeAdminDto("a", "A"),
                new EventAttendeeAdminDto("c", "C"),
            });

        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("attendingEvent", "evt-42"), CancellationToken.None);

        Assert.Equal(2, ids.Count);
        Assert.Contains("a", ids);
        Assert.Contains("c", ids);
        Assert.DoesNotContain("b", ids);
        eventsMock.Verify(e => e.GetEventAttendeesAsync("evt-42"), Times.Once);
    }

    [Fact]
    public async Task Resolve_attendingEvent_with_no_value_returns_empty()
    {
        var (resolver, _, eventsMock) = Build();
        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("attendingEvent", null), CancellationToken.None);
        Assert.Empty(ids);
        eventsMock.Verify(e => e.GetEventAttendeesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_attendingEvent_deduplicates()
    {
        var (resolver, _, _) = Build(
            attendees: new[]
            {
                new EventAttendeeAdminDto("a", "A"),
                new EventAttendeeAdminDto("a", "A again"),
                new EventAttendeeAdminDto("b", "B"),
            });
        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("attendingEvent", "e"), CancellationToken.None);
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public async Task Resolve_minRank_activeMember_excludes_novices()
    {
        var (resolver, _, _) = Build(new[]
        {
            User("nov", UserRank.Novice),
            User("act", UserRank.ActiveMember),
            User("friend", UserRank.FriendOfAloe),
            User("crew", UserRank.AloeCrew),
        });

        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("minRank", "activeMember"), CancellationToken.None);

        Assert.Equal(3, ids.Count);
        Assert.DoesNotContain("nov", ids);
        Assert.Contains("act", ids);
        Assert.Contains("friend", ids);
        Assert.Contains("crew", ids);
    }

    [Fact]
    public async Task Resolve_minRank_aloeCrew_only_includes_crew_and_staff()
    {
        var (resolver, _, _) = Build(new[]
        {
            User("nov", UserRank.Novice),
            User("crew", UserRank.AloeCrew),
            User("mod", UserRank.Novice, StaffRole.Moderator),
            User("admin", UserRank.Novice, StaffRole.Admin),
        });

        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("minRank", "aloeCrew"), CancellationToken.None);

        // crew (level 3), moderator (level 4), admin (level 5) all >= 3
        Assert.Equal(3, ids.Count);
        Assert.Contains("crew", ids);
        Assert.Contains("mod", ids);
        Assert.Contains("admin", ids);
        Assert.DoesNotContain("nov", ids);
    }

    [Fact]
    public async Task Resolve_minRank_with_no_value_returns_empty()
    {
        var (resolver, _, _) = Build(new[] { User("u1", UserRank.AloeCrew) });
        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("minRank", null), CancellationToken.None);
        Assert.Empty(ids);
    }

    [Fact]
    public async Task Resolve_staffRole_admin_returns_only_admins()
    {
        var (resolver, _, _) = Build(new[]
        {
            User("regular", UserRank.AloeCrew),
            User("mod", UserRank.Novice, StaffRole.Moderator),
            User("admin1", UserRank.Novice, StaffRole.Admin),
            User("admin2", UserRank.ActiveMember, StaffRole.Admin),
        });

        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("staffRole", "admin"), CancellationToken.None);

        Assert.Equal(2, ids.Count);
        Assert.Contains("admin1", ids);
        Assert.Contains("admin2", ids);
    }

    [Fact]
    public async Task Resolve_staffRole_moderator_excludes_admins()
    {
        var (resolver, _, _) = Build(new[]
        {
            User("mod", UserRank.Novice, StaffRole.Moderator),
            User("admin", UserRank.Novice, StaffRole.Admin),
            User("regular", UserRank.Novice, StaffRole.None),
        });

        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("staffRole", "moderator"), CancellationToken.None);

        Assert.Single(ids);
        Assert.Equal("mod", ids[0]);
    }

    [Fact]
    public async Task Resolve_staffRole_invalid_value_returns_empty()
    {
        var (resolver, _, _) = Build(new[]
        {
            User("admin", UserRank.Novice, StaffRole.Admin),
        });
        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("staffRole", "wizard"), CancellationToken.None);
        Assert.Empty(ids);
    }

    [Fact]
    public async Task Resolve_staffRole_with_no_value_returns_empty()
    {
        var (resolver, _, _) = Build(new[] { User("admin", UserRank.Novice, StaffRole.Admin) });
        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("staffRole", null), CancellationToken.None);
        Assert.Empty(ids);
    }

    [Fact]
    public async Task Resolve_unknown_type_returns_empty()
    {
        var (resolver, _, _) = Build(new[] { User("u1"), User("u2") });
        var ids = await resolver.ResolveAsync(new BroadcastAudienceDto("nonexistent-type", "value"), CancellationToken.None);
        Assert.Empty(ids);
    }

    [Fact]
    public async Task Resolve_null_audience_returns_empty()
    {
        var (resolver, _, _) = Build();
        var ids = await resolver.ResolveAsync(null!, CancellationToken.None);
        Assert.Empty(ids);
    }
}
