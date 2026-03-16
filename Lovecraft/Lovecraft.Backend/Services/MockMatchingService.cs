using Lovecraft.Common.DTOs.Matching;
using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockMatchingService : IMatchingService
{
    private readonly IChatService _chatService;

    public MockMatchingService(IChatService chatService)
    {
        _chatService = chatService;
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

            return new LikeResponseDto
            {
                Like = like,
                IsMatch = true,
                Match = match
            };
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
