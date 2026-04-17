using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;
using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockForumService : IForumService
{
    private readonly IUserService _userService;

    public MockForumService(IUserService userService)
    {
        _userService = userService;
    }

    public Task<List<ForumSectionDto>> GetSectionsAsync()
    {
        return Task.FromResult(MockDataStore.ForumSections);
    }

    public Task<List<ForumTopicDto>> GetTopicsAsync(string sectionId)
    {
        var topics = MockDataStore.ForumTopics
            .Where(t => t.SectionId == sectionId)
            .OrderByDescending(t => t.IsPinned)
            .ToList();
        return Task.FromResult(topics);
    }

    public Task<ForumTopicDto?> GetTopicByIdAsync(string topicId)
    {
        var topic = MockDataStore.ForumTopics.FirstOrDefault(t => t.Id == topicId);
        return Task.FromResult(topic);
    }

    public async Task<List<ForumReplyDto>> GetRepliesAsync(string topicId)
    {
        var stored = MockDataStore.ForumReplies
            .Where(r => r.TopicId == topicId)
            .OrderBy(r => r.CreatedAt)
            .ToList();

        var authorIds = stored.Select(r => r.AuthorId).Where(id => !string.IsNullOrEmpty(id)).Distinct();
        var authors = new Dictionary<string, UserDto?>();
        foreach (var id in authorIds)
            authors[id] = await _userService.GetUserByIdAsync(id);

        return stored.Select(r => new ForumReplyDto
        {
            Id = r.Id,
            TopicId = r.TopicId,
            AuthorId = r.AuthorId,
            AuthorName = r.AuthorName,
            AuthorAvatar = r.AuthorAvatar,
            Content = r.Content,
            CreatedAt = r.CreatedAt,
            Likes = r.Likes,
            ImageUrls = r.ImageUrls,
            AuthorRank = authors.GetValueOrDefault(r.AuthorId)?.Rank ?? UserRank.Novice,
            AuthorStaffRole = authors.GetValueOrDefault(r.AuthorId)?.StaffRole ?? StaffRole.None,
        }).ToList();
    }

    public async Task<ForumReplyDto> CreateReplyAsync(string topicId, string authorId, string authorName, string content, List<string>? imageUrls = null)
    {
        var authorAvatar = MockDataStore.Users.FirstOrDefault(u => u.Id == authorId)?.ProfileImage;
        var reply = new ForumReplyDto
        {
            Id = $"r_{Guid.NewGuid():N}",
            TopicId = topicId,
            AuthorId = authorId,
            AuthorName = authorName,
            AuthorAvatar = string.IsNullOrEmpty(authorAvatar) ? null : authorAvatar,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            Likes = 0,
            ImageUrls = imageUrls ?? new List<string>()
        };
        MockDataStore.ForumReplies.Add(reply);

        var topic = MockDataStore.ForumTopics.FirstOrDefault(t => t.Id == topicId);
        if (topic != null)
        {
            topic.ReplyCount++;
            topic.UpdatedAt = reply.CreatedAt;
        }

        await _userService.IncrementCounterAsync(authorId, UserCounter.ReplyCount);

        var author = await _userService.GetUserByIdAsync(authorId);
        return new ForumReplyDto
        {
            Id = reply.Id,
            TopicId = reply.TopicId,
            AuthorId = reply.AuthorId,
            AuthorName = reply.AuthorName,
            AuthorAvatar = reply.AuthorAvatar,
            Content = reply.Content,
            CreatedAt = reply.CreatedAt,
            Likes = reply.Likes,
            ImageUrls = reply.ImageUrls,
            AuthorRank = author?.Rank ?? UserRank.Novice,
            AuthorStaffRole = author?.StaffRole ?? StaffRole.None,
        };
    }

    public Task<ForumTopicDto> CreateTopicAsync(
        string sectionId, string authorId, string authorName, string title, string content)
    {
        var section = MockDataStore.ForumSections.FirstOrDefault(s => s.Id == sectionId)
            ?? throw new KeyNotFoundException($"Section '{sectionId}' not found.");

        var topic = new ForumTopicDto
        {
            Id = Guid.NewGuid().ToString(),
            SectionId = sectionId,
            Title = title,
            Content = content,
            AuthorId = authorId,
            AuthorName = authorName,
            AuthorAvatar = null,
            IsPinned = false,
            IsLocked = false,
            ReplyCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        MockDataStore.ForumTopics.Add(topic);
        section.TopicCount++;
        return Task.FromResult(topic);
    }

    public Task<ForumTopicDto> CreateEventTopicAsync(string eventId, string eventName)
    {
        var topic = new ForumTopicDto
        {
            Id = $"event-topic-{eventId}",
            SectionId = "events",
            Title = eventName,
            Content = $"Обсуждение события: {eventName}",
            AuthorId = "system",
            AuthorName = "AloeVera",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ReplyCount = 0,
            IsPinned = false
        };
        MockDataStore.ForumTopics.Add(topic);
        return Task.FromResult(topic);
    }
}
