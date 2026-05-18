using Lovecraft.Backend.Services.Notifications;
using Xunit;

namespace Lovecraft.UnitTests;

public class PresenceTrackerTests
{
    [Fact]
    public void Initially_empty()
    {
        var t = new PresenceTracker();
        Assert.False(t.IsInGroup("chat-1", "u1"));
    }

    [Fact]
    public void Join_makes_user_present_in_group()
    {
        var t = new PresenceTracker();
        t.Join("chat-1", "u1");
        Assert.True(t.IsInGroup("chat-1", "u1"));
    }

    [Fact]
    public void Leave_removes_user_when_refcount_drops_to_zero()
    {
        var t = new PresenceTracker();
        t.Join("chat-1", "u1");
        t.Leave("chat-1", "u1");
        Assert.False(t.IsInGroup("chat-1", "u1"));
    }

    [Fact]
    public void Two_tabs_keep_user_present_until_both_leave()
    {
        var t = new PresenceTracker();
        t.Join("chat-1", "u1");
        t.Join("chat-1", "u1");
        t.Leave("chat-1", "u1");
        Assert.True(t.IsInGroup("chat-1", "u1"));
        t.Leave("chat-1", "u1");
        Assert.False(t.IsInGroup("chat-1", "u1"));
    }

    [Fact]
    public void Leave_unknown_user_is_noop()
    {
        var t = new PresenceTracker();
        t.Leave("chat-1", "ghost");
        Assert.False(t.IsInGroup("chat-1", "ghost"));
    }
}
