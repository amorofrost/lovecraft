using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Matching;
using Lovecraft.Common.Enums;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Tests for MockMatchingService covering the intersection-based match
/// computation and auto-chat-creation on mutual like.
/// Each test gets a clean MockDataStore.Likes slate via constructor/Dispose.
/// </summary>
[Collection("MatchingTests")]
public class MatchingTests : IDisposable
{
    public MatchingTests()
    {
        MockDataStore.Likes = new List<LikeDto>();
        MockDataStore.UserActivity.Clear();
    }

    public void Dispose()
    {
        MockDataStore.Likes = new List<LikeDto>();
        MockDataStore.UserActivity.Clear();
    }

    private static (MockMatchingService matching, MockChatService chat) CreateServices()
    {
        var chat = new MockChatService();
        var userSvc = new MockUserService(new MockAppConfigService());
        var matching = new MockMatchingService(chat, userSvc);
        return (matching, chat);
    }

    // ── CreateLike ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateLike_OneWay_IsNotMatch()
    {
        var (svc, _) = CreateServices();

        var result = await svc.CreateLikeAsync("alice", "bob");

        Assert.False(result.IsMatch);
        Assert.Null(result.Match);
    }

    [Fact]
    public async Task CreateLike_MutualLike_IsMatch()
    {
        var (svc, _) = CreateServices();

        await svc.CreateLikeAsync("alice", "bob");
        var result = await svc.CreateLikeAsync("bob", "alice");

        Assert.True(result.IsMatch);
        Assert.NotNull(result.Match);
    }

    [Fact]
    public async Task CreateLike_MutualLike_MatchContainsBothUsers()
    {
        var (svc, _) = CreateServices();

        await svc.CreateLikeAsync("alice", "bob");
        var result = await svc.CreateLikeAsync("bob", "alice");

        Assert.Contains("alice", result.Match!.Users);
        Assert.Contains("bob", result.Match!.Users);
    }

    [Fact]
    public async Task CreateLike_MutualLike_AutoCreatesChat()
    {
        var (matching, chat) = CreateServices();

        await matching.CreateLikeAsync("alice", "bob");
        await matching.CreateLikeAsync("bob", "alice");

        // The chat service should now have a chat between alice and bob
        var aliceChats = await chat.GetChatsAsync("alice");
        Assert.Contains(aliceChats, c =>
            c.Participants.Contains("alice") && c.Participants.Contains("bob"));
    }

    [Fact]
    public async Task CreateLike_AlreadyLiked_ReturnsExistingLike()
    {
        var (svc, _) = CreateServices();

        var first = await svc.CreateLikeAsync("alice", "bob");
        var second = await svc.CreateLikeAsync("alice", "bob");

        Assert.Equal(first.Like.Id, second.Like.Id);
    }

    [Fact]
    public async Task CreateLike_AlreadyLiked_DoesNotCreateDuplicateChat()
    {
        var (matching, chat) = CreateServices();

        // Create a mutual match (chat auto-created)
        await matching.CreateLikeAsync("alice", "bob");
        await matching.CreateLikeAsync("bob", "alice");

        // Resending alice's like should not create a second chat
        await matching.CreateLikeAsync("alice", "bob");

        var aliceChats = await chat.GetChatsAsync("alice");
        var chatsBetweenThem = aliceChats.Where(c =>
            c.Participants.Contains("alice") && c.Participants.Contains("bob")).ToList();
        Assert.Single(chatsBetweenThem);
    }

    // ── GetSentLikes ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSentLikes_ReturnsOnlyLikesSentByUser()
    {
        var (svc, _) = CreateServices();
        await svc.CreateLikeAsync("alice", "bob");
        await svc.CreateLikeAsync("alice", "carol");
        await svc.CreateLikeAsync("bob", "alice");

        var sent = await svc.GetSentLikesAsync("alice");

        Assert.All(sent, l => Assert.Equal("alice", l.FromUserId));
        Assert.Equal(2, sent.Count);
    }

    // ── GetReceivedLikes ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetReceivedLikes_ReturnsOnlyLikesReceivedByUser()
    {
        var (svc, _) = CreateServices();
        await svc.CreateLikeAsync("bob", "alice");
        await svc.CreateLikeAsync("carol", "alice");
        await svc.CreateLikeAsync("alice", "bob");

        var received = await svc.GetReceivedLikesAsync("alice");

        Assert.All(received, l => Assert.Equal("alice", l.ToUserId));
        Assert.Equal(2, received.Count);
    }

    // ── GetMatches ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMatches_OneWayLike_ReturnsNoMatches()
    {
        var (svc, _) = CreateServices();
        await svc.CreateLikeAsync("alice", "bob");

        var matches = await svc.GetMatchesAsync("alice");

        Assert.Empty(matches);
    }

    [Fact]
    public async Task GetMatches_MutualLike_ReturnsMatch()
    {
        var (svc, _) = CreateServices();
        await svc.CreateLikeAsync("alice", "bob");
        await svc.CreateLikeAsync("bob", "alice");

        var aliceMatches = await svc.GetMatchesAsync("alice");
        var bobMatches   = await svc.GetMatchesAsync("bob");

        Assert.Single(aliceMatches);
        Assert.Single(bobMatches);
    }

    [Fact]
    public async Task GetMatches_MutualLike_MatchContainsOtherUser()
    {
        var (svc, _) = CreateServices();
        await svc.CreateLikeAsync("alice", "bob");
        await svc.CreateLikeAsync("bob", "alice");

        var matches = await svc.GetMatchesAsync("alice");

        Assert.Contains(matches, m => m.Users.Contains("bob"));
    }

    [Fact]
    public async Task GetMatches_MixedLikes_ReturnsOnlyMutual()
    {
        var (svc, _) = CreateServices();
        // alice → bob (mutual)
        await svc.CreateLikeAsync("alice", "bob");
        await svc.CreateLikeAsync("bob", "alice");
        // alice → carol (one-way, not a match)
        await svc.CreateLikeAsync("alice", "carol");

        var matches = await svc.GetMatchesAsync("alice");

        Assert.Single(matches);
        Assert.Contains(matches, m => m.Users.Contains("bob"));
        Assert.DoesNotContain(matches, m => m.Users.Contains("carol"));
    }

    [Fact]
    public async Task GetMatches_MatchIdIsDeterministic()
    {
        var (svc, _) = CreateServices();
        await svc.CreateLikeAsync("alice", "bob");
        await svc.CreateLikeAsync("bob", "alice");

        var fromAlice = (await svc.GetMatchesAsync("alice")).First();
        var fromBob   = (await svc.GetMatchesAsync("bob")).First();

        // Both sides should compute the same stable match ID
        Assert.Equal(fromAlice.Id, fromBob.Id);
    }

    // ── Counter hooks (LikesReceived + MatchCount) ────────────────────────────

    [Fact]
    public async Task CreateLike_IncrementsTargetLikesReceived()
    {
        MockDataStore.UserActivity.Clear();
        var userSvc = new MockUserService(new MockAppConfigService());
        var service = new MockMatchingService(new MockChatService(), userSvc);

        await service.CreateLikeAsync("1", "2");

        Assert.Equal(1, MockDataStore.UserActivity.TryGetValue("2", out var a) ? a.LikesReceived : 0);
        MockDataStore.UserActivity.Clear();
    }

    [Fact]
    public async Task MutualLike_IncrementsMatchCount_OnBoth()
    {
        MockDataStore.UserActivity.Clear();
        MockDataStore.Matches = new List<MatchDto>();
        var userSvc = new MockUserService(new MockAppConfigService());
        var service = new MockMatchingService(new MockChatService(), userSvc);

        await service.CreateLikeAsync("1", "2"); // like 1→2
        await service.CreateLikeAsync("2", "1"); // reverse → match

        Assert.Equal(1, MockDataStore.UserActivity["1"].MatchCount);
        Assert.Equal(1, MockDataStore.UserActivity["2"].MatchCount);
        MockDataStore.UserActivity.Clear();
    }
}

[Collection("MatchingTests")]
public class MatchingNotificationTests
{
    public MatchingNotificationTests()
    {
        MockDataStore.Likes = new List<LikeDto>();
        MockDataStore.Matches = new List<MatchDto>();
        MockDataStore.UserActivity.Clear();
        // Reset AnonymousLikes on first user (may be mutated by anonymous test)
        var first = MockDataStore.Users.FirstOrDefault();
        if (first?.Settings != null)
            first.Settings.AnonymousLikes = false;
    }

    private static MockMatchingService BuildService(Mock<INotificationProducer> producer)
    {
        var chatService = new MockChatService();
        var userSvc = new MockUserService(new MockAppConfigService());
        return new MockMatchingService(chatService, userSvc, producer.Object);
    }

    [Fact]
    public async Task Non_mutual_like_fires_LikeReceived_to_recipient()
    {
        var producer = new Mock<INotificationProducer>();
        var svc = BuildService(producer);

        await svc.CreateLikeAsync("u-from", "u-to");

        producer.Verify(p => p.ProduceAsync(
            "u-to", NotificationType.LikeReceived,
            "u-from", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Mutual_like_fires_MatchCreated_to_both_users()
    {
        var producer = new Mock<INotificationProducer>();
        var svc = BuildService(producer);
        await svc.CreateLikeAsync("u-a", "u-b");        // first like — fires LikeReceived to u-b
        producer.Invocations.Clear();

        await svc.CreateLikeAsync("u-b", "u-a");        // mutual — should fire MatchCreated to both

        producer.Verify(p => p.ProduceAsync(
            "u-a", NotificationType.MatchCreated, "u-b",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
        producer.Verify(p => p.ProduceAsync(
            "u-b", NotificationType.MatchCreated, "u-a",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Anonymous_like_uses_null_actorId_and_anonymous_true_in_payload()
    {
        var producer = new Mock<INotificationProducer>();
        var svc = BuildService(producer);

        // Seed sender with AnonymousLikes=true via mock user data
        var sender = MockDataStore.Users.First();
        sender.Settings.AnonymousLikes = true;
        await svc.CreateLikeAsync(sender.Id, "u-target");

        producer.Verify(p => p.ProduceAsync(
            "u-target",
            NotificationType.LikeReceived,
            (string?)null,                                       // actorId omitted
            It.Is<string>(s => s.Contains("\"anonymous\":true")),
            It.IsAny<string>(),
            It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Self_action_not_attempted_at_producer_layer()
    {
        var producer = new Mock<INotificationProducer>();
        var svc = BuildService(producer);

        // Self-like: the matching service may already reject this at controller; if it goes through,
        // the producer's self-action skip handles it. Either way: producer should never write a row
        // for self-action. We verify the producer is either not called, or called with same recipient/actor
        // (which the producer will internally suppress — verified in NotificationProducerTests).
        // For this test, we assert producer.ProduceAsync is NOT called with (recipient == actor).
        await svc.CreateLikeAsync("u-self", "u-self");

        producer.Verify(p => p.ProduceAsync(
            It.Is<string>(rid => rid == "u-self"),
            It.IsAny<NotificationType>(),
            It.Is<string?>(aid => aid == "u-self"),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>()), Times.Never);
    }
}
