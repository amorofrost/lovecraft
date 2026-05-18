using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Azure;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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

public class AzureNotificationServiceTests
{
    private static Mock<TableClient> EmptyTable() => new();

    [Fact]
    public async Task Create_writes_row_with_inverted_ticks_rowkey()
    {
        var notifs = EmptyTable();
        var outbox = EmptyTable();
        NotificationEntity? written = null;
        notifs.Setup(t => t.AddEntityAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationEntity, CancellationToken>((e, _) => written = e)
            .ReturnsAsync(new Mock<Response>().Object);

        var svc = new AzureNotificationService(notifs.Object, outbox.Object,
            NullLogger<AzureNotificationService>.Instance);

        var n = await svc.CreateAsync("u1", NotificationType.LikeReceived, "actor", "{}", "src-1");

        Assert.NotNull(written);
        Assert.Equal("u1", written!.PartitionKey);
        Assert.StartsWith(string.Empty, written.RowKey); // 19-digit inverted ticks + "_" + id
        Assert.Equal(20 + n.Id.Length, written.RowKey.Length);
        Assert.Equal("src-1", written.SourceEventId);
    }

    [Fact]
    public async Task EnqueueOutbox_writes_to_pending_partition()
    {
        var notifs = EmptyTable();
        var outbox = EmptyTable();
        NotificationOutboxEntity? written = null;
        outbox.Setup(t => t.AddEntityAsync(It.IsAny<NotificationOutboxEntity>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationOutboxEntity, CancellationToken>((e, _) => written = e)
            .ReturnsAsync(new Mock<Response>().Object);

        var svc = new AzureNotificationService(notifs.Object, outbox.Object,
            NullLogger<AzureNotificationService>.Instance);

        await svc.EnqueueOutboxAsync("u1", "nid-1",
            NotificationChannel.Telegram, NotificationFrequency.Immediate, DateTime.UtcNow);

        Assert.NotNull(written);
        Assert.Equal("OUTBOX_Telegram_PENDING", written!.PartitionKey);
        Assert.Equal("u1", written.UserId);
        Assert.Equal("nid-1", written.NotificationId);
    }
}
