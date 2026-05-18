using System.Text.Json;
using Lovecraft.Common.DTOs.Matching;
using Lovecraft.Common.Enums;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services.Notifications;

namespace Lovecraft.Backend.Services;

public class MockMatchingService : IMatchingService
{
    private readonly IChatService _chatService;
    private readonly IUserService _userService;
    private readonly INotificationProducer? _producer;

    public MockMatchingService(IChatService chatService, IUserService userService, INotificationProducer? producer = null)
    {
        _chatService = chatService;
        _userService = userService;
        _producer = producer;
    }

    public async Task<LikeResponseDto> CreateLikeAsync(string fromUserId, string toUserId)
    {
        // Check if like already exists
        var existingLike = MockDataStore.Likes.FirstOrDefault(l =>
            l.FromUserId == fromUserId && l.ToUserId == toUserId);

        if (existingLike != null)
        {
            return new LikeResponseDto
            {
                Like = existingLike,
                IsMatch = existingLike.IsMatch
            };
        }

        // Create new like
        var like = new LikeDto
        {
            Id = Guid.NewGuid().ToString(),
            FromUserId = fromUserId,
            ToUserId = toUserId,
            CreatedAt = DateTime.UtcNow,
            IsMatch = false
        };

        MockDataStore.Likes.Add(like);

        await _userService.IncrementCounterAsync(toUserId, UserCounter.LikesReceived);

        // Check if reverse like exists (match)
        var reverseLike = MockDataStore.Likes.FirstOrDefault(l =>
            l.FromUserId == toUserId && l.ToUserId == fromUserId);

        if (reverseLike != null)
        {
            // It's a match! Auto-create the 1:1 chat
            like.IsMatch = true;
            reverseLike.IsMatch = true;

            var match = new MatchDto
            {
                Id = Guid.NewGuid().ToString(),
                Users = new List<string> { fromUserId, toUserId },
                CreatedAt = DateTime.UtcNow
            };

            await _chatService.GetOrCreateChatAsync(fromUserId, toUserId);

            await _userService.IncrementCounterAsync(fromUserId, UserCounter.MatchCount);
            await _userService.IncrementCounterAsync(toUserId, UserCounter.MatchCount);

            // Fire MatchCreated notification to both users (skip self-action)
            if (_producer is not null && fromUserId != toUserId)
            {
                var lex = string.CompareOrdinal(fromUserId, toUserId) < 0
                    ? (fromUserId, toUserId)
                    : (toUserId, fromUserId);
                var matchSourceId = $"match-{lex.Item1}-{lex.Item2}";
                var matchPayload = JsonSerializer.Serialize(new { matchId = matchSourceId });

                await _producer.ProduceAsync(toUserId, NotificationType.MatchCreated, fromUserId, matchPayload, matchSourceId);
                await _producer.ProduceAsync(fromUserId, NotificationType.MatchCreated, toUserId, matchPayload, matchSourceId);
            }

            return new LikeResponseDto
            {
                Like = like,
                IsMatch = true,
                Match = match
            };
        }

        // Non-mutual like: fire LikeReceived notification to recipient (skip self-action)
        if (_producer is not null && fromUserId != toUserId)
        {
            var sender = MockDataStore.Users.FirstOrDefault(u => u.Id == fromUserId);
            var anonymous = sender?.Settings?.AnonymousLikes ?? false;
            var payloadJson = JsonSerializer.Serialize(new
            {
                likeId = like.Id,
                anonymous,
            });
            await _producer.ProduceAsync(
                recipientUserId: toUserId,
                type: NotificationType.LikeReceived,
                actorId: anonymous ? null : fromUserId,
                payloadJson: payloadJson,
                sourceEventId: like.Id);
        }

        return new LikeResponseDto
        {
            Like = like,
            IsMatch = false
        };
    }

    public Task<List<LikeDto>> GetSentLikesAsync(string userId)
    {
        var likes = MockDataStore.Likes
            .Where(l => l.FromUserId == userId)
            .ToList();
        return Task.FromResult(likes);
    }

    public Task<List<LikeDto>> GetReceivedLikesAsync(string userId)
    {
        var likes = MockDataStore.Likes
            .Where(l => l.ToUserId == userId)
            .ToList();
        return Task.FromResult(likes);
    }

    public Task<List<MatchDto>> GetMatchesAsync(string userId)
    {
        // Compute from intersection: users I liked AND who liked me back
        var likedUserIds = MockDataStore.Likes
            .Where(l => l.FromUserId == userId)
            .Select(l => l.ToUserId)
            .ToHashSet();

        var matches = MockDataStore.Likes
            .Where(l => l.ToUserId == userId && likedUserIds.Contains(l.FromUserId))
            .Select(l =>
            {
                var sorted = new[] { userId, l.FromUserId }.OrderBy(x => x).ToArray();
                return new MatchDto
                {
                    Id = $"{sorted[0]}_{sorted[1]}",
                    Users = new List<string> { userId, l.FromUserId },
                    CreatedAt = l.CreatedAt
                };
            })
            .ToList();

        return Task.FromResult(matches);
    }
}
