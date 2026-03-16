using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Matching;
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
    }

    public void Dispose()
    {
        MockDataStore.Likes = new List<LikeDto>();
    }

    private static (MockMatchingService matching, MockChatService chat) CreateServices()
    {
        var chat = new MockChatService();
        var matching = new MockMatchingService(chat);
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
}
