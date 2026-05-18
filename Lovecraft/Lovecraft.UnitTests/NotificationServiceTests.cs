using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Common.Enums;
using Xunit;

namespace Lovecraft.UnitTests;

public class MockNotificationServiceTests
{
    public MockNotificationServiceTests() { MockDataStore.Notifications.Clear(); }

    [Fact]
    public async Task Create_returns_dto_with_assigned_id_and_now()
    {
        var svc = new MockNotificationService();
        var before = DateTime.UtcNow;

        var n = await svc.CreateAsync("u1", NotificationType.LikeReceived, "actor", "{}", "like-1");

        Assert.False(string.IsNullOrEmpty(n.Id));
        Assert.Equal("u1", n.UserId);
        Assert.Equal(NotificationType.LikeReceived, n.Type);
        Assert.True(n.CreatedAtUtc >= before);
        Assert.Null(n.ReadAtUtc);
    }

    [Fact]
    public async Task List_returns_newest_first()
    {
        var svc = new MockNotificationService();
        await svc.CreateAsync("u1", NotificationType.LikeReceived, null, "{}", "a");
        await Task.Delay(5);
        await svc.CreateAsync("u1", NotificationType.MatchCreated, null, "{}", "b");

        var list = await svc.ListAsync("u1", 10, null);

        Assert.Equal(2, list.Count);
        Assert.Equal(NotificationType.MatchCreated, list[0].Type);
        Assert.Equal(NotificationType.LikeReceived, list[1].Type);
    }

    [Fact]
    public async Task UnreadCount_counts_only_unread()
    {
        var svc = new MockNotificationService();
        var n1 = await svc.CreateAsync("u1", NotificationType.LikeReceived, null, "{}", "a");
        await svc.CreateAsync("u1", NotificationType.MatchCreated, null, "{}", "b");
        await svc.MarkReadAsync("u1", n1.Id);

        var count = await svc.UnreadCountAsync("u1");

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task MarkAllRead_sets_all_unread()
    {
        var svc = new MockNotificationService();
        await svc.CreateAsync("u1", NotificationType.LikeReceived, null, "{}", "a");
        await svc.CreateAsync("u1", NotificationType.MatchCreated, null, "{}", "b");

        var updated = await svc.MarkAllReadAsync("u1");

        Assert.Equal(2, updated);
        Assert.Equal(0, await svc.UnreadCountAsync("u1"));
    }

    [Fact]
    public async Task Dismiss_hides_from_list()
    {
        var svc = new MockNotificationService();
        var n = await svc.CreateAsync("u1", NotificationType.LikeReceived, null, "{}", "a");

        await svc.DismissAsync("u1", n.Id);
        var list = await svc.ListAsync("u1", 10, null);

        Assert.Empty(list);
    }

    [Fact]
    public async Task RecentForDedup_finds_match_within_window()
    {
        var svc = new MockNotificationService();
        await svc.CreateAsync("u1", NotificationType.MessageReceived, "actor", "{}", "msg-1");

        var hits = await svc.RecentForDedupAsync("u1", NotificationType.MessageReceived, "actor", "msg-1", 60);

        Assert.Single(hits);
    }

    [Fact]
    public async Task RecentForDedup_ignores_different_sourceEventId()
    {
        var svc = new MockNotificationService();
        await svc.CreateAsync("u1", NotificationType.MessageReceived, "actor", "{}", "msg-1");

        var hits = await svc.RecentForDedupAsync("u1", NotificationType.MessageReceived, "actor", "msg-2", 60);

        Assert.Empty(hits);
    }
}
