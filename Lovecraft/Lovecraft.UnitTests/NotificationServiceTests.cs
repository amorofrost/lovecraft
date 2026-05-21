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

    [Fact]
    public async Task Create_populates_ActorName_and_ActorAvatar_when_IUserService_provided()
    {
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetUserByIdAsync("actor-1")).ReturnsAsync(new Lovecraft.Common.DTOs.Users.UserDto
        {
            Id = "actor-1", Name = "Anna", ProfileImage = "https://cdn/anna.jpg",
        });
        var svc = new MockNotificationService(users.Object);

        var n = await svc.CreateAsync("u1", NotificationType.LikeReceived, "actor-1", "{}", "like-1");

        Assert.Equal("Anna", n.ActorName);
        Assert.Equal("https://cdn/anna.jpg", n.ActorAvatar);
    }

    [Fact]
    public async Task Create_leaves_ActorName_null_when_actorId_null()
    {
        var users = new Mock<IUserService>();
        var svc = new MockNotificationService(users.Object);

        var n = await svc.CreateAsync("u1", NotificationType.CommunityBroadcast, null, "{}", "bc-1");

        Assert.Null(n.ActorName);
        users.Verify(u => u.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task List_populates_ActorName_for_each_row()
    {
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetUserByIdAsync("actor-1")).ReturnsAsync(new Lovecraft.Common.DTOs.Users.UserDto { Id = "actor-1", Name = "Anna" });
        users.Setup(u => u.GetUserByIdAsync("actor-2")).ReturnsAsync(new Lovecraft.Common.DTOs.Users.UserDto { Id = "actor-2", Name = "Boris" });
        var svc = new MockNotificationService(users.Object);

        await svc.CreateAsync("u1", NotificationType.LikeReceived, "actor-1", "{}", "like-1");
        await svc.CreateAsync("u1", NotificationType.MessageReceived, "actor-2", "{}", "msg-1");

        var list = await svc.ListAsync("u1", 10, null);

        Assert.Equal(2, list.Count);
        Assert.Contains(list, n => n.ActorId == "actor-1" && n.ActorName == "Anna");
        Assert.Contains(list, n => n.ActorId == "actor-2" && n.ActorName == "Boris");
    }

    [Fact]
    public async Task List_dedupes_actor_lookups_across_rows()
    {
        var users = new Mock<IUserService>();
        users.Setup(u => u.GetUserByIdAsync("actor-1")).ReturnsAsync(new Lovecraft.Common.DTOs.Users.UserDto { Id = "actor-1", Name = "Anna" });
        var svc = new MockNotificationService(users.Object);

        // 3 notifications from the same actor — should only resolve once.
        await svc.CreateAsync("u1", NotificationType.LikeReceived, "actor-1", "{}", "like-1");
        await svc.CreateAsync("u1", NotificationType.MessageReceived, "actor-1", "{}", "msg-1");
        await svc.CreateAsync("u1", NotificationType.MessageReceived, "actor-1", "{}", "msg-2");
        users.Invocations.Clear();

        await svc.ListAsync("u1", 10, null);

        users.Verify(u => u.GetUserByIdAsync("actor-1"), Times.Once);
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

    [Fact]
    public async Task Create_sets_IsRead_false_and_IsDismissed_false()
    {
        var notifs = EmptyTable();
        var outbox = EmptyTable();
        NotificationEntity? written = null;
        notifs.Setup(t => t.AddEntityAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationEntity, CancellationToken>((e, _) => written = e)
            .ReturnsAsync(new Mock<Response>().Object);

        var svc = new AzureNotificationService(notifs.Object, outbox.Object,
            NullLogger<AzureNotificationService>.Instance);

        await svc.CreateAsync("u1", NotificationType.LikeReceived, "actor", "{}", "src-1");

        Assert.NotNull(written);
        Assert.False(written!.IsRead);
        Assert.False(written.IsDismissed);
    }

    [Fact]
    public async Task ListAsync_with_cursor_includes_RowKey_gt_in_filter()
    {
        var notifs = EmptyTable();
        var outbox = EmptyTable();
        var emptyCursor = "12345678901234567890_abc";
        string? capturedFilter = null;

        var emptyPage = Azure.Page<NotificationEntity>.FromValues(
            new List<NotificationEntity>(), null, Mock.Of<Response>());
        notifs.Setup(t => t.QueryAsync<NotificationEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, int?, IEnumerable<string>?, CancellationToken>((f, _, _, _) => capturedFilter = f)
            .Returns(Azure.AsyncPageable<NotificationEntity>.FromPages(new[] { emptyPage }));

        var svc = new AzureNotificationService(notifs.Object, outbox.Object,
            NullLogger<AzureNotificationService>.Instance);

        await svc.ListAsync("u1", 20, emptyCursor);

        Assert.NotNull(capturedFilter);
        Assert.Contains($"RowKey gt '{emptyCursor}'", capturedFilter);
    }
}
