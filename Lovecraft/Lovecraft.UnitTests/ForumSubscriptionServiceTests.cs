using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Exercises MockForumSubscriptionService: subscribe/unsubscribe semantics, idempotency,
/// per-topic isolation, and the subscriber list view used by reply fanout.
/// </summary>
public class ForumSubscriptionServiceTests : IDisposable
{
    public ForumSubscriptionServiceTests()
    {
        MockDataStore.ForumSubscriptions.Clear();
    }

    public void Dispose()
    {
        MockDataStore.ForumSubscriptions.Clear();
    }

    [Fact]
    public async Task Subscribe_then_IsSubscribed_returns_true()
    {
        var svc = new MockForumSubscriptionService();
        await svc.SubscribeAsync("u1", "t1", "manual");

        Assert.True(await svc.IsSubscribedAsync("u1", "t1"));
    }

    [Fact]
    public async Task Unsubscribe_then_IsSubscribed_returns_false()
    {
        var svc = new MockForumSubscriptionService();
        await svc.SubscribeAsync("u1", "t1", "manual");
        var removed = await svc.UnsubscribeAsync("u1", "t1");

        Assert.True(removed);
        Assert.False(await svc.IsSubscribedAsync("u1", "t1"));
    }

    [Fact]
    public async Task Subscribe_is_idempotent()
    {
        var svc = new MockForumSubscriptionService();
        await svc.SubscribeAsync("u1", "t1", "manual");
        await svc.SubscribeAsync("u1", "t1", "manual"); // second call must not throw

        Assert.True(await svc.IsSubscribedAsync("u1", "t1"));
        var subs = await svc.GetSubscribersAsync("t1");
        Assert.Single(subs);
        Assert.Equal("u1", subs[0]);
    }

    [Fact]
    public async Task GetSubscribers_returns_all_userIds_for_topic()
    {
        var svc = new MockForumSubscriptionService();
        await svc.SubscribeAsync("u1", "t1", "create");
        await svc.SubscribeAsync("u2", "t1", "reply");
        await svc.SubscribeAsync("u3", "t1", "manual");

        var subs = await svc.GetSubscribersAsync("t1");

        Assert.Equal(3, subs.Count);
        Assert.Contains("u1", subs);
        Assert.Contains("u2", subs);
        Assert.Contains("u3", subs);
    }

    [Fact]
    public async Task Different_topics_do_not_leak_subscribers()
    {
        var svc = new MockForumSubscriptionService();
        await svc.SubscribeAsync("u1", "t1", "create");
        await svc.SubscribeAsync("u2", "t2", "create");

        var t1subs = await svc.GetSubscribersAsync("t1");
        var t2subs = await svc.GetSubscribersAsync("t2");

        Assert.Single(t1subs);
        Assert.Equal("u1", t1subs[0]);
        Assert.Single(t2subs);
        Assert.Equal("u2", t2subs[0]);

        Assert.False(await svc.IsSubscribedAsync("u1", "t2"));
        Assert.False(await svc.IsSubscribedAsync("u2", "t1"));
    }

    [Fact]
    public async Task Unsubscribe_when_not_subscribed_returns_false()
    {
        var svc = new MockForumSubscriptionService();

        // Topic that's never seen any subscriber.
        var removed = await svc.UnsubscribeAsync("u1", "t-ghost");
        Assert.False(removed);

        // Topic that exists in the dictionary but doesn't have this user.
        await svc.SubscribeAsync("u2", "t1", "manual");
        var removed2 = await svc.UnsubscribeAsync("u1", "t1");
        Assert.False(removed2);
    }

    [Fact]
    public async Task GetSubscribers_for_unknown_topic_returns_empty()
    {
        var svc = new MockForumSubscriptionService();
        var subs = await svc.GetSubscribersAsync("t-nonexistent");

        Assert.Empty(subs);
    }
}
