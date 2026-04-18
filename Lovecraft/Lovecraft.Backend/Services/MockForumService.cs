using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;
using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockForumService : IForumService
{
    private const string EventSectionId = "events";
    private readonly IUserService _userService;
    private readonly IEventService _eventService;

    public MockForumService(IUserService userService, IEventService eventService)
    {
        _userService = userService;
        _eventService = eventService;
    }

    public Task<List<ForumSectionDto>> GetSectionsAsync()
    {
        var list = MockDataStore.ForumSections
            .Where(s => !s.Id.Equals(EventSectionId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(list);
    }

    public async Task<List<EventDiscussionSectionDto>> GetEventDiscussionSectionsAsync(string userId, bool isElevated)
    {
        var events = await _eventService.GetEventsAsync();
        var list = new List<EventDiscussionSectionDto>();
        foreach (var e in events)
        {
            if (e.Visibility == EventVisibility.SecretHidden && !isElevated && !e.Attendees.Contains(userId))
                continue;

            var n = MockDataStore.ForumTopics.Count(t =>
                t.SectionId == EventSectionId &&
                string.Equals(ResolveEventId(t), e.Id, StringComparison.Ordinal));

            list.Add(new EventDiscussionSectionDto
            {
                EventId = e.Id,
                Title = e.Title,
                Date = e.Date,
                Visibility = e.Visibility,
                IsAttending = e.Attendees.Contains(userId),
                TopicCount = n,
            });
        }

        return list.OrderBy(x => x.Date).ToList();
    }

    public async Task<List<ForumTopicDto>?> GetEventDiscussionTopicsAsync(string userId, string eventId, bool isElevated)
    {
        var ev = isElevated
            ? await _eventService.GetEventByIdAdminAsync(eventId)
            : await _eventService.GetEventByIdAsync(eventId);
        if (ev == null)
            return null;
        if (ev.Visibility == EventVisibility.SecretHidden && !isElevated && !ev.Attendees.Contains(userId))
            return null;

        var topics = MockDataStore.ForumTopics
            .Where(t => t.SectionId == EventSectionId && string.Equals(ResolveEventId(t), eventId, StringComparison.Ordinal))
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.UpdatedAt)
            .ToList();
        return topics;
    }

    private static string? ResolveEventId(ForumTopicDto t)
    {
        if (!string.IsNullOrEmpty(t.EventId))
            return t.EventId;
        if (t.Id.StartsWith("evt-", StringComparison.Ordinal) && t.Id.Length > 4)
            return t.Id.Substring(4);
        if (t.Id.StartsWith("event-topic-", StringComparison.Ordinal) && t.Id.Length > "event-topic-".Length)
            return t.Id.Substring("event-topic-".Length);
        return null;
    }

    public Task<List<ForumTopicDto>> GetTopicsAsync(string sectionId)
    {
        if (sectionId.Equals(EventSectionId, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new List<ForumTopicDto>());

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
        string sectionId, string authorId, string authorName, string title, string content,
        bool? noviceVisible = null, bool? noviceCanReply = null)
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
            UpdatedAt = DateTime.UtcNow,
            NoviceVisible = noviceVisible ?? true,
            NoviceCanReply = noviceCanReply ?? true
        };

        MockDataStore.ForumTopics.Add(topic);
        section.TopicCount++;
        return Task.FromResult(topic);
    }

    public Task<ForumTopicDto?> UpdateTopicAsync(string topicId, UpdateTopicRequestDto update)
    {
        var topic = MockDataStore.ForumTopics.FirstOrDefault(t => t.Id == topicId);
        if (topic is null) return Task.FromResult<ForumTopicDto?>(null);

        if (!string.IsNullOrWhiteSpace(update.Title))
            topic.Title = update.Title!;
        if (!string.IsNullOrWhiteSpace(update.Content))
            topic.Content = update.Content!;
        if (update.NoviceVisible.HasValue) topic.NoviceVisible = update.NoviceVisible.Value;
        if (update.NoviceCanReply.HasValue) topic.NoviceCanReply = update.NoviceCanReply.Value;
        if (update.IsPinned.HasValue) topic.IsPinned = update.IsPinned.Value;
        if (update.IsLocked.HasValue) topic.IsLocked = update.IsLocked.Value;
        topic.UpdatedAt = DateTime.UtcNow;

        return Task.FromResult<ForumTopicDto?>(topic);
    }

    public async Task<ForumTopicDto> CreateEventDiscussionTopicAsync(
        string eventId,
        string title,
        string content,
        string authorId,
        string authorName,
        bool? noviceVisible = null,
        bool? noviceCanReply = null)
    {
        var ev = await _eventService.GetEventByIdAdminAsync(eventId).ConfigureAwait(false);
        if (ev is null)
            throw new KeyNotFoundException($"Event '{eventId}' not found.");

        var topic = new ForumTopicDto
        {
            Id = Guid.NewGuid().ToString(),
            SectionId = EventSectionId,
            EventId = eventId,
            Title = title,
            Content = content,
            AuthorId = authorId,
            AuthorName = authorName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ReplyCount = 0,
            IsPinned = false,
            NoviceVisible = noviceVisible ?? true,
            NoviceCanReply = noviceCanReply ?? true
        };
        MockDataStore.ForumTopics.Add(topic);
        return topic;
    }

    public Task<bool> DeleteTopicAsync(string topicId)
    {
        var topic = MockDataStore.ForumTopics.FirstOrDefault(t => t.Id == topicId);
        if (topic is null)
            return Task.FromResult(false);
        MockDataStore.ForumTopics.Remove(topic);
        MockDataStore.ForumReplies.RemoveAll(r => r.TopicId == topicId);
        if (!string.Equals(topic.SectionId, EventSectionId, StringComparison.OrdinalIgnoreCase))
        {
            var section = MockDataStore.ForumSections.FirstOrDefault(s => s.Id == topic.SectionId);
            if (section is not null && section.TopicCount > 0)
                section.TopicCount--;
        }
        return Task.FromResult(true);
    }

    public async Task<IReadOnlyList<string>> DeleteTopicsForEventAsync(string eventId)
    {
        var ids = MockDataStore.ForumTopics
            .Where(t => t.SectionId == EventSectionId && string.Equals(ResolveEventId(t), eventId, StringComparison.Ordinal))
            .Select(t => t.Id)
            .ToList();
        foreach (var id in ids)
            await DeleteTopicAsync(id).ConfigureAwait(false);
        return ids;
    }

    public Task<ForumTopicDto> CreateEventTopicAsync(string eventId, string eventName)
    {
        var topic = new ForumTopicDto
        {
            Id = $"event-topic-{eventId}",
            SectionId = EventSectionId,
            EventId = eventId,
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
