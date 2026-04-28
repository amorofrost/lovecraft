using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Models;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Helpers;

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
            .OrderBy(s => s.OrderIndex)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(list);
    }

    public async Task<List<EventDiscussionSectionDto>> GetEventDiscussionSectionsAsync(string userId, bool isElevated)
    {
        var events = await _eventService.GetEventsAsync();
        var list = new List<EventDiscussionSectionDto>();
        foreach (var e in events)
        {
            if (!EventForumAccess.CanViewEventDiscussionSummary(e, userId, isElevated))
                continue;

            var n = MockDataStore.ForumTopics.Count(t =>
                t.SectionId == EventSectionId &&
                string.Equals(ResolveEventId(t), e.Id, StringComparison.Ordinal) &&
                EventTopicAccess.CanViewEventTopic(e, t, userId, isElevated));

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

    public async Task<PagedResult<ForumTopicDto>?> GetEventDiscussionTopicsAsync(string userId, string eventId, bool isElevated, int page = 1)
    {
        var ev = isElevated
            ? await _eventService.GetEventByIdAdminAsync(eventId)
            : await _eventService.GetEventByIdAsync(eventId);
        if (ev == null)
            return null;
        if (!EventForumAccess.CanViewEventDiscussionSummary(ev, userId, isElevated))
            return null;

        var topics = MockDataStore.ForumTopics
            .Where(t => t.SectionId == EventSectionId && string.Equals(ResolveEventId(t), eventId, StringComparison.Ordinal))
            .Where(t => EventTopicAccess.CanViewEventTopic(ev, t, userId, isElevated))
            .ToList();
        var pageSize = page == 1
            ? PaginationConfig.Defaults.TopicsInitial
            : PaginationConfig.Defaults.TopicsBatch;
        var sorted = topics
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.UpdatedAt)
            .ToList();
        var offset = page == 1
            ? 0
            : PaginationConfig.Defaults.TopicsInitial + (page - 2) * PaginationConfig.Defaults.TopicsBatch;
        var batch = sorted.Skip(offset).Take(pageSize + 1).ToList();
        return new PagedResult<ForumTopicDto>
        {
            Items    = batch.Take(pageSize).ToList(),
            PageSize = pageSize,
            HasMore  = batch.Count > pageSize,
            Total    = sorted.Count,
        };
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

    public Task<PagedResult<ForumTopicDto>> GetTopicsAsync(string sectionId, int page = 1)
    {
        if (sectionId.Equals(EventSectionId, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new PagedResult<ForumTopicDto>());

        var pageSize = page == 1
            ? PaginationConfig.Defaults.TopicsInitial
            : PaginationConfig.Defaults.TopicsBatch;

        var sorted = MockDataStore.ForumTopics
            .Where(t => t.SectionId == sectionId)
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.UpdatedAt)
            .ToList();

        var offset = page == 1
            ? 0
            : PaginationConfig.Defaults.TopicsInitial + (page - 2) * PaginationConfig.Defaults.TopicsBatch;

        var batch = sorted.Skip(offset).Take(pageSize + 1).ToList();
        var hasMore = batch.Count > pageSize;

        return Task.FromResult(new PagedResult<ForumTopicDto>
        {
            Items    = batch.Take(pageSize).ToList(),
            PageSize = pageSize,
            HasMore  = hasMore,
            Total    = sorted.Count,
        });
    }

    public Task<ForumTopicDto?> GetTopicByIdAsync(string topicId)
    {
        var topic = MockDataStore.ForumTopics.FirstOrDefault(t => t.Id == topicId);
        return Task.FromResult(topic);
    }

    public async Task<PagedResult<ForumReplyDto>> GetRepliesAsync(string topicId, string? cursor = null)
    {
        var pageSize = cursor == null
            ? PaginationConfig.Defaults.RepliesInitial
            : PaginationConfig.Defaults.RepliesBatch;

        var stored = MockDataStore.ForumReplies
            .Where(r => r.TopicId == topicId)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        var startIndex = 0;
        if (cursor != null)
        {
            var idx = stored.FindIndex(r => r.Id == cursor);
            startIndex = idx >= 0 ? idx + 1 : 0;
        }

        var batch = stored.Skip(startIndex).Take(pageSize + 1).ToList();
        var hasMore = batch.Count > pageSize;
        var page = batch.Take(pageSize).ToList();

        var authorIds = page.Select(r => r.AuthorId).Where(id => !string.IsNullOrEmpty(id)).Distinct();
        var authors = new Dictionary<string, UserDto?>();
        foreach (var id in authorIds)
            authors[id] = await _userService.GetUserByIdAsync(id);

        var items = new List<ForumReplyDto>();
        foreach (var r in page)
        {
            var (urls, total) = string.IsNullOrEmpty(r.AuthorId)
                ? (new List<string>(), 0)
                : await _eventService.GetUserEventBadgePreviewAsync(r.AuthorId);
            var author = authors.GetValueOrDefault(r.AuthorId);
            items.Add(new ForumReplyDto
            {
                Id                         = r.Id,
                TopicId                    = r.TopicId,
                AuthorId                   = r.AuthorId,
                AuthorName                 = r.AuthorName,
                AuthorAvatar               = r.AuthorAvatar,
                Content                    = r.Content,
                CreatedAt                  = r.CreatedAt,
                Likes                      = r.Likes,
                ImageUrls                  = r.ImageUrls ?? new(),
                AuthorRank                 = author?.Rank ?? UserRank.Novice,
                AuthorStaffRole            = author?.StaffRole ?? StaffRole.None,
                AuthorEventBadgeImageUrls  = urls,
                AuthorEventBadgeTotalCount = total,
            });
        }

        return new PagedResult<ForumReplyDto>
        {
            Items      = items,
            PageSize   = pageSize,
            HasMore    = hasMore,
            NextCursor = items.Count > 0 ? items.Last().Id : null,
        };
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
        var (badgeUrls, badgeTotal) = await _eventService.GetUserEventBadgePreviewAsync(authorId);
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
            AuthorEventBadgeImageUrls = badgeUrls,
            AuthorEventBadgeTotalCount = badgeTotal,
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
            NoviceCanReply = noviceCanReply ?? true,
            EventTopicVisibility = EventTopicVisibility.Public,
            AllowedUserIds = new List<string>(),
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
        if (update.EventTopicVisibility.HasValue) topic.EventTopicVisibility = update.EventTopicVisibility.Value;
        if (update.AllowedUserIds is not null)
            topic.AllowedUserIds = update.AllowedUserIds.Distinct().ToList();
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
        bool? noviceCanReply = null,
        EventTopicVisibility? eventTopicVisibility = null,
        IReadOnlyList<string>? allowedUserIds = null)
    {
        var ev = await _eventService.GetEventByIdAdminAsync(eventId).ConfigureAwait(false);
        if (ev is null)
            throw new KeyNotFoundException($"Event '{eventId}' not found.");

        var vis = eventTopicVisibility ?? EventTopicVisibility.Public;
        var ids = allowedUserIds != null ? allowedUserIds.Distinct().ToList() : new List<string>();
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
            NoviceCanReply = noviceCanReply ?? true,
            EventTopicVisibility = vis,
            AllowedUserIds = ids,
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
        var now = DateTime.UtcNow;
        var publicTopic = new ForumTopicDto
        {
            Id = $"event-topic-{eventId}",
            SectionId = EventSectionId,
            EventId = eventId,
            Title = eventName,
            Content = $"Обсуждение события: {eventName}",
            AuthorId = "system",
            AuthorName = "AloeVera",
            CreatedAt = now,
            UpdatedAt = now,
            ReplyCount = 0,
            IsPinned = false,
            EventTopicVisibility = EventTopicVisibility.Public,
            AllowedUserIds = new List<string>(),
        };
        MockDataStore.ForumTopics.Add(publicTopic);

        var attendeesTopic = new ForumTopicDto
        {
            Id = $"event-attendees-{eventId}",
            SectionId = EventSectionId,
            EventId = eventId,
            Title = $"{eventName} — для участников",
            Content = $"Обсуждение только для зарегистрированных участников события: {eventName}",
            AuthorId = "system",
            AuthorName = "AloeVera",
            CreatedAt = now,
            UpdatedAt = now,
            ReplyCount = 0,
            IsPinned = false,
            EventTopicVisibility = EventTopicVisibility.AttendeesOnly,
            AllowedUserIds = new List<string>(),
        };
        MockDataStore.ForumTopics.Add(attendeesTopic);

        return Task.FromResult(publicTopic);
    }

    public Task<ForumSectionDto> CreateSectionAsync(string id, string name, string description, string minRank)
    {
        if (id.Equals(EventSectionId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Reserved section id", nameof(id));
        if (MockDataStore.ForumSections.Any(s => s.Id == id))
            throw new InvalidOperationException($"Section '{id}' already exists.");

        var maxOrder = MockDataStore.ForumSections
            .Where(s => !s.Id.Equals(EventSectionId, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.OrderIndex)
            .DefaultIfEmpty(-1)
            .Max();

        var dto = new ForumSectionDto
        {
            Id = id,
            Name = name,
            Description = description ?? string.Empty,
            TopicCount = 0,
            OrderIndex = maxOrder + 1,
            MinRank = NormalizeSectionMinRank(minRank),
        };
        MockDataStore.ForumSections.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<ForumSectionDto?> UpdateSectionAsync(string sectionId, string? name, string? description, string? minRank)
    {
        if (sectionId.Equals(EventSectionId, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<ForumSectionDto?>(null);
        var section = MockDataStore.ForumSections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null)
            return Task.FromResult<ForumSectionDto?>(null);
        if (!string.IsNullOrWhiteSpace(name))
            section.Name = name.Trim();
        if (description is not null)
            section.Description = description;
        if (!string.IsNullOrWhiteSpace(minRank))
            section.MinRank = NormalizeSectionMinRank(minRank);
        return Task.FromResult<ForumSectionDto?>(section);
    }

    public async Task<bool> DeleteSectionAsync(string sectionId)
    {
        if (sectionId.Equals(EventSectionId, StringComparison.OrdinalIgnoreCase))
            return false;
        var section = MockDataStore.ForumSections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null)
            return false;
        var topics = MockDataStore.ForumTopics.Where(t => t.SectionId == sectionId).ToList();
        foreach (var t in topics)
            await DeleteTopicAsync(t.Id).ConfigureAwait(false);
        MockDataStore.ForumSections.Remove(section);
        return true;
    }

    public Task<bool> ReorderSectionsAsync(IReadOnlyList<string> orderedSectionIds)
    {
        var nonEvent = MockDataStore.ForumSections
            .Where(s => !s.Id.Equals(EventSectionId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (orderedSectionIds.Count != nonEvent.Count)
            return Task.FromResult(false);
        var set = new HashSet<string>(nonEvent.Select(s => s.Id), StringComparer.Ordinal);
        foreach (var id in orderedSectionIds)
        {
            if (!set.Contains(id))
                return Task.FromResult(false);
        }

        for (var i = 0; i < orderedSectionIds.Count; i++)
        {
            var s = MockDataStore.ForumSections.First(x => x.Id == orderedSectionIds[i]);
            s.OrderIndex = i;
        }

        return Task.FromResult(true);
    }

    private static string NormalizeSectionMinRank(string minRank)
    {
        if (string.IsNullOrWhiteSpace(minRank))
            return "novice";
        var r = minRank.Trim();
        return r switch
        {
            "novice" or "activeMember" or "friendOfAloe" or "aloeCrew" => r,
            _ => "novice",
        };
    }
}
