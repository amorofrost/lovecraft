using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Matching;
using Lovecraft.Common.DTOs.Store;
using Lovecraft.Common.DTOs.Blog;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Chats;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services;

public enum UserCounter
{
    ReplyCount,
    LikesReceived,
    EventsAttended,
    MatchCount,
}

public interface IUserService
{
    Task<List<UserDto>> GetUsersAsync(int skip = 0, int take = 10);
    Task<UserDto?> GetUserByIdAsync(string userId);
    Task<UserDto> UpdateUserAsync(string userId, UserDto user);
    Task IncrementCounterAsync(string userId, UserCounter counter, int delta = 1);
    Task SetStaffRoleAsync(string userId, StaffRole role);
    Task SetRankOverrideAsync(string userId, UserRank? rank);
}

public interface IEventService
{
    Task<List<EventDto>> GetEventsAsync();
    Task<EventDto?> GetEventByIdAsync(string eventId);
    Task<List<EventDto>> GetEventsAdminAsync();
    Task<EventDto?> GetEventByIdAdminAsync(string eventId);
    Task<EventDto> CreateEventAsync(AdminEventWriteDto dto);
    Task<EventDto?> UpdateEventAsync(string eventId, AdminEventWriteDto dto);
    Task<bool> DeleteEventAsync(string eventId);
    Task<bool> SetEventArchivedAsync(string eventId, bool archived);
    Task<List<EventAttendeeAdminDto>> GetEventAttendeesAsync(string eventId);
    Task<bool> RemoveEventAttendeeAsync(string eventId, string userId);
    Task<bool> RegisterForEventAsync(string userId, string eventId);
    Task<bool> UnregisterFromEventAsync(string userId, string eventId);

    /// <summary>Adds the user to the event&apos;s &quot;interested&quot; list (idempotent).</summary>
    Task<bool> AddEventInterestAsync(string userId, string eventId);

    /// <summary>Removes the user from the event&apos;s &quot;interested&quot; list.</summary>
    Task<bool> RemoveEventInterestAsync(string userId, string eventId);

    Task SetForumTopicIdAsync(string eventId, string forumTopicId);

    /// <summary>Events the user has registered for (newest first). Includes archived events.</summary>
    Task<List<EventDto>> GetEventsAttendedByUserAsync(string userId);

    /// <summary>Badge image URLs from attended events (newest first), for compact UI.</summary>
    Task<(List<string> PreviewUrls, int TotalCount)> GetUserEventBadgePreviewAsync(string userId);
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
    Task<List<EventDiscussionSectionDto>> GetEventDiscussionSectionsAsync(string userId, bool isElevated);
    Task<List<ForumTopicDto>?> GetEventDiscussionTopicsAsync(string userId, string eventId, bool isElevated);
    Task<List<ForumTopicDto>> GetTopicsAsync(string sectionId);
    Task<ForumTopicDto?> GetTopicByIdAsync(string topicId);
    Task<List<ForumReplyDto>> GetRepliesAsync(string topicId);
    Task<ForumReplyDto> CreateReplyAsync(string topicId, string authorId, string authorName, string content, List<string>? imageUrls = null);
    Task<ForumTopicDto> CreateEventTopicAsync(string eventId, string eventName);
    Task<ForumTopicDto> CreateTopicAsync(
        string sectionId,
        string authorId,
        string authorName,
        string title,
        string content,
        bool? noviceVisible = null,
        bool? noviceCanReply = null);
    Task<ForumTopicDto?> UpdateTopicAsync(string topicId, UpdateTopicRequestDto update);
    Task<ForumTopicDto> CreateEventDiscussionTopicAsync(
        string eventId,
        string title,
        string content,
        string authorId,
        string authorName,
        bool? noviceVisible = null,
        bool? noviceCanReply = null);
    Task<bool> DeleteTopicAsync(string topicId);
    Task<IReadOnlyList<string>> DeleteTopicsForEventAsync(string eventId);
}

public interface IChatService
{
    Task<List<ChatDto>> GetChatsAsync(string userId);
    Task<ChatDto> GetOrCreateChatAsync(string userId, string targetUserId);
    // TODO(Task 14): remove fully-qualified name once Matching.MessageDto ambiguity is resolved
    Task<List<Lovecraft.Common.DTOs.Chats.MessageDto>> GetMessagesAsync(string chatId, string userId, int page = 1, int pageSize = 50);
    Task<Lovecraft.Common.DTOs.Chats.MessageDto> SendMessageAsync(string chatId, string userId, string content, List<string>? imageUrls = null);
    Task<bool> ValidateAccessAsync(string chatId, string userId);
}

public interface IImageService
{
    Task<string> UploadProfileImageAsync(string userId, Stream imageStream, string contentType);
    Task<string> UploadContentImageAsync(string userId, Stream imageStream, string contentType);
}
