using Lovecraft.Common.DTOs.Matching;
using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockMatchingService : IMatchingService
{
    public Task<LikeResponseDto> CreateLikeAsync(string fromUserId, string toUserId)
    {
        // Check if like already exists
        var existingLike = MockDataStore.Likes.FirstOrDefault(l =>
            l.FromUserId == fromUserId && l.ToUserId == toUserId);

        if (existingLike != null)
        {
            return Task.FromResult(new LikeResponseDto
            {
                Like = existingLike,
                IsMatch = existingLike.IsMatch
            });
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
            // It's a match!
            like.IsMatch = true;
            reverseLike.IsMatch = true;

            var match = new MatchDto
            {
                Id = Guid.NewGuid().ToString(),
                Users = new List<string> { fromUserId, toUserId },
                CreatedAt = DateTime.UtcNow
            };

            MockDataStore.Matches.Add(match);

            return Task.FromResult(new LikeResponseDto
            {
                Like = like,
                IsMatch = true,
                Match = match
            });
        }

        return Task.FromResult(new LikeResponseDto
        {
            Like = like,
            IsMatch = false
        });
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
        var matches = MockDataStore.Matches
            .Where(m => m.Users.Contains(userId))
            .ToList();
        return Task.FromResult(matches);
    }
}
