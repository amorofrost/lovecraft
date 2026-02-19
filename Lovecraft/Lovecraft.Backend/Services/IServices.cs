using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Matching;
using Lovecraft.Common.DTOs.Store;
using Lovecraft.Common.DTOs.Blog;
using Lovecraft.Common.DTOs.Forum;

namespace Lovecraft.Backend.Services;

public interface IUserService
{
    Task<List<UserDto>> GetUsersAsync(int skip = 0, int take = 10);
    Task<UserDto?> GetUserByIdAsync(string userId);
    Task<UserDto> UpdateUserAsync(string userId, UserDto user);
}

public interface IEventService
{
    Task<List<EventDto>> GetEventsAsync();
    Task<EventDto?> GetEventByIdAsync(string eventId);
    Task<bool> RegisterForEventAsync(string userId, string eventId);
    Task<bool> UnregisterFromEventAsync(string userId, string eventId);
}

public interface IMatchingService
{
    Task<LikeResponseDto> CreateLikeAsync(string fromUserId, string toUserId);
    Task<List<LikeDto>> GetSentLikesAsync(string userId);
    Task<List<LikeDto>> GetReceivedLikesAsync(string userId);
    Task<List<MatchDto>> GetMatchesAsync(string userId);
}

public interface IStoreService
{
    Task<List<StoreItemDto>> GetStoreItemsAsync();
    Task<StoreItemDto?> GetStoreItemByIdAsync(string itemId);
}

public interface IBlogService
{
    Task<List<BlogPostDto>> GetBlogPostsAsync();
    Task<BlogPostDto?> GetBlogPostByIdAsync(string postId);
}

public interface IForumService
{
    Task<List<ForumSectionDto>> GetSectionsAsync();
    Task<List<ForumTopicDto>> GetTopicsAsync(string sectionId);
    Task<ForumTopicDto?> GetTopicByIdAsync(string topicId);
    Task<List<ForumReplyDto>> GetRepliesAsync(string topicId);
    Task<ForumReplyDto> CreateReplyAsync(string topicId, string authorId, string authorName, string content);
}
