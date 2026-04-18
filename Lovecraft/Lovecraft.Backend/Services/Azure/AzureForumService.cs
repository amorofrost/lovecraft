using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services.Azure;

public class AzureForumService : IForumService
{
    private const string EventSectionId = "events";

    private readonly TableClient _sectionsTable;
    private readonly TableClient _topicsTable;
    private readonly TableClient _topicIndexTable;
    private readonly TableClient _repliesTable;
    private readonly TableClient _usersTable;
    private readonly IUserService _userService;
    private readonly IEventService _eventService;
    private readonly ILogger<AzureForumService> _logger;

    public AzureForumService(
        TableServiceClient tableServiceClient,
        IUserService userService,
        IEventService eventService,
        ILogger<AzureForumService> logger)
    {
        _userService = userService;
        _eventService = eventService;
        _logger = logger;
        _sectionsTable = tableServiceClient.GetTableClient(TableNames.ForumSections);
        _topicsTable = tableServiceClient.GetTableClient(TableNames.ForumTopics);
        _topicIndexTable = tableServiceClient.GetTableClient(TableNames.ForumTopicIndex);
        _repliesTable = tableServiceClient.GetTableClient(TableNames.ForumReplies);
        _usersTable = tableServiceClient.GetTableClient(TableNames.Users);

        Task.WhenAll(
            _sectionsTable.CreateIfNotExistsAsync(),
            _topicsTable.CreateIfNotExistsAsync(),
            _topicIndexTable.CreateIfNotExistsAsync(),
            _repliesTable.CreateIfNotExistsAsync()
        ).GetAwaiter().GetResult();
    }

    private async Task<string?> GetAuthorAvatarAsync(string authorId)
    {
        try
        {
            var pk = Storage.Entities.UserEntity.GetPartitionKey(authorId);
            var response = await _usersTable.GetEntityAsync<Storage.Entities.UserEntity>(pk, authorId);
            var url = response.Value.ProfileImage;
            return string.IsNullOrEmpty(url) ? null : url;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<ForumSectionDto>> GetSectionsAsync()
    {
        var results = new List<ForumSectionDto>();
        await foreach (var entity in _sectionsTable.QueryAsync<ForumSectionEntity>(filter: "PartitionKey eq 'FORUM'"))
        {
            results.Add(ToSectionDto(entity));
        }

        return results
            .Where(s => !s.Id.Equals(EventSectionId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Id)
            .ToList();
    }

    public async Task<List<EventDiscussionSectionDto>> GetEventDiscussionSectionsAsync(string userId, bool isElevated)
    {
        var events = await _eventService.GetEventsAsync();
        var list = new List<EventDiscussionSectionDto>();
        foreach (var e in events)
        {
            if (e.Visibility == EventVisibility.SecretHidden && !isElevated && !e.Attendees.Contains(userId))
                continue;

            var n = await CountTopicsForEventAsync(e.Id);
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
        var ev = await _eventService.GetEventByIdAsync(eventId);
        if (ev == null)
            return null;
        if (ev.Visibility == EventVisibility.SecretHidden && !isElevated && !ev.Attendees.Contains(userId))
            return null;

        var pk = ForumTopicEntity.GetPartitionKey(EventSectionId);
        var escapedPk = pk.Replace("'", "''");
        var results = new List<ForumTopicDto>();
        await foreach (var entity in _topicsTable.QueryAsync<ForumTopicEntity>(
                     filter: $"PartitionKey eq '{escapedPk}'"))
        {
            if (!TopicEntityBelongsToEvent(entity, eventId))
                continue;
            results.Add(ToTopicDto(entity));
        }

        return results
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.UpdatedAt)
            .ToList();
    }

    private async Task<int> CountTopicsForEventAsync(string eventId)
    {
        var pk = ForumTopicEntity.GetPartitionKey(EventSectionId);
        var escapedPk = pk.Replace("'", "''");
        var n = 0;
        await foreach (var entity in _topicsTable.QueryAsync<ForumTopicEntity>(
                     filter: $"PartitionKey eq '{escapedPk}'"))
        {
            if (TopicEntityBelongsToEvent(entity, eventId))
                n++;
        }

        return n;
    }

    private static bool TopicEntityBelongsToEvent(ForumTopicEntity e, string eventId) =>
        string.Equals(ResolveEventIdFromTopicEntity(e), eventId, StringComparison.Ordinal);

    /// <summary>Uses <see cref="ForumTopicEntity.EventId"/> or legacy topic id patterns.</summary>
    private static string? ResolveEventIdFromTopicEntity(ForumTopicEntity e)
    {
        if (!string.IsNullOrEmpty(e.EventId))
            return e.EventId;
        if (e.RowKey.StartsWith("evt-", StringComparison.Ordinal) && e.RowKey.Length > 4)
            return e.RowKey.Substring(4);
        if (e.RowKey.StartsWith("event-topic-", StringComparison.Ordinal) && e.RowKey.Length > "event-topic-".Length)
            return e.RowKey["event-topic-".Length..];
        return null;
    }

    public async Task<List<ForumTopicDto>> GetTopicsAsync(string sectionId)
    {
        if (sectionId.Equals(EventSectionId, StringComparison.OrdinalIgnoreCase))
            return new List<ForumTopicDto>();

        var pk = ForumTopicEntity.GetPartitionKey(sectionId);
        var results = new List<ForumTopicDto>();
        await foreach (var entity in _topicsTable.QueryAsync<ForumTopicEntity>(
            filter: $"PartitionKey eq '{pk}'"))
        {
            results.Add(ToTopicDto(entity));
        }
        return results;
    }

    public async Task<ForumTopicDto?> GetTopicByIdAsync(string topicId)
    {
        // Point-query the index to get sectionId
        ForumTopicIndexEntity indexEntity;
        try
        {
            var indexResponse = await _topicIndexTable.GetEntityAsync<ForumTopicIndexEntity>("TOPICINDEX", topicId);
            indexEntity = indexResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        // Point-query the topic using sectionId
        try
        {
            var pk = ForumTopicEntity.GetPartitionKey(indexEntity.SectionId);
            var topicResponse = await _topicsTable.GetEntityAsync<ForumTopicEntity>(pk, topicId);
            return ToTopicDto(topicResponse.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<ForumReplyDto>> GetRepliesAsync(string topicId)
    {
        var pk = ForumReplyEntity.GetPartitionKey(topicId);
        var entities = new List<ForumReplyEntity>();
        // Results come back newest-first due to reversed-ticks RK ordering
        await foreach (var entity in _repliesTable.QueryAsync<ForumReplyEntity>(
            filter: $"PartitionKey eq '{pk}'"))
        {
            entities.Add(entity);
        }

        // Fetch current avatar for each unique author so stale cached URLs don't persist
        // after a user updates their profile picture.
        var authorIds = entities.Select(e => e.AuthorId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var avatars = new Dictionary<string, string?>();
        var userInfo = new Dictionary<string, UserDto?>();
        foreach (var authorId in authorIds)
        {
            avatars[authorId] = await GetAuthorAvatarAsync(authorId);
            userInfo[authorId] = await _userService.GetUserByIdAsync(authorId);
        }

        return entities.Select(e => ToReplyDto(
            e,
            avatars.GetValueOrDefault(e.AuthorId),
            userInfo.GetValueOrDefault(e.AuthorId))).ToList();
    }

    public async Task<ForumReplyDto> CreateReplyAsync(string topicId, string authorId, string authorName, string content, List<string>? imageUrls = null)
    {
        var now = DateTime.UtcNow;
        var replyId = Guid.NewGuid().ToString();

        var authorAvatar = await GetAuthorAvatarAsync(authorId);

        var replyEntity = new ForumReplyEntity
        {
            PartitionKey = ForumReplyEntity.GetPartitionKey(topicId),
            RowKey = ForumReplyEntity.BuildRowKey(now, replyId),
            ReplyId = replyId,
            TopicId = topicId,
            AuthorId = authorId,
            AuthorName = authorName,
            AuthorAvatar = authorAvatar ?? string.Empty,
            Content = content,
            CreatedAt = now,
            Likes = 0,
            ImageUrls = JsonSerializer.Serialize(imageUrls ?? new List<string>())
        };

        await _repliesTable.UpsertEntityAsync(replyEntity);

        // Update topic: increment ReplyCount and UpdatedAt (read-modify-write; race condition acceptable)
        var topicDto = await GetTopicByIdAsync(topicId);
        if (topicDto != null)
        {
            try
            {
                var pk = ForumTopicEntity.GetPartitionKey(topicDto.SectionId);
                var topicResponse = await _topicsTable.GetEntityAsync<ForumTopicEntity>(pk, topicId);
                var topicEntity = topicResponse.Value;
                topicEntity.ReplyCount++;
                topicEntity.UpdatedAt = now;
                await _topicsTable.UpdateEntityAsync(topicEntity, topicEntity.ETag);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(ex, "Failed to update reply count for topic {TopicId}", topicId);
            }
        }

        try
        {
            await _userService.IncrementCounterAsync(authorId, UserCounter.ReplyCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to increment {Counter} for user {UserId}",
                UserCounter.ReplyCount, authorId);
        }

        var author = await _userService.GetUserByIdAsync(authorId);
        return ToReplyDto(replyEntity, authorAvatar, author);
    }

    private static ForumSectionDto ToSectionDto(ForumSectionEntity entity) => new ForumSectionDto
    {
        Id = entity.RowKey,
        Name = entity.Name,
        Description = entity.Description,
        TopicCount = entity.TopicCount,
        MinRank = string.IsNullOrWhiteSpace(entity.MinRank) ? "novice" : entity.MinRank
    };

    private static ForumTopicDto ToTopicDto(ForumTopicEntity entity) => new ForumTopicDto
    {
        Id = entity.RowKey,
        SectionId = entity.SectionId,
        EventId = string.IsNullOrEmpty(entity.EventId) ? null : entity.EventId,
        Title = entity.Title,
        Content = entity.Content,
        AuthorId = entity.AuthorId,
        AuthorName = entity.AuthorName,
        AuthorAvatar = string.IsNullOrEmpty(entity.AuthorAvatar) ? null : entity.AuthorAvatar,
        IsPinned = entity.IsPinned,
        IsLocked = entity.IsLocked,
        ReplyCount = entity.ReplyCount,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
        MinRank = string.IsNullOrWhiteSpace(entity.MinRank) ? "novice" : entity.MinRank,
        NoviceVisible = entity.NoviceVisible ?? true,
        NoviceCanReply = entity.NoviceCanReply ?? true
    };

    public async Task<ForumTopicDto> CreateEventTopicAsync(string eventId, string eventName)
    {
        var topicId = $"event-topic-{eventId}";
        var now = DateTime.UtcNow;
        const string sectionId = "events";

        var topicEntity = new ForumTopicEntity
        {
            PartitionKey = ForumTopicEntity.GetPartitionKey(sectionId),
            RowKey = topicId,
            SectionId = sectionId,
            EventId = eventId,
            Title = eventName,
            Content = $"Обсуждение события: {eventName}",
            AuthorId = "system",
            AuthorName = "AloeVera",
            AuthorAvatar = string.Empty,
            IsPinned = false,
            IsLocked = false,
            ReplyCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _topicsTable.UpsertEntityAsync(topicEntity);

        // Upsert index entry so GetTopicByIdAsync can resolve this topic
        var indexEntity = new ForumTopicIndexEntity
        {
            PartitionKey = "TOPICINDEX",
            RowKey = topicId,
            SectionId = sectionId
        };
        await _topicIndexTable.UpsertEntityAsync(indexEntity);

        return new ForumTopicDto
        {
            Id = topicId,
            SectionId = sectionId,
            EventId = eventId,
            Title = eventName,
            Content = $"Обсуждение события: {eventName}",
            AuthorId = "system",
            AuthorName = "AloeVera",
            CreatedAt = now,
            UpdatedAt = now,
            ReplyCount = 0,
            IsPinned = false
        };
    }

    public async Task<ForumTopicDto> CreateTopicAsync(
        string sectionId, string authorId, string authorName, string title, string content,
        bool? noviceVisible = null, bool? noviceCanReply = null)
    {
        // 1. Verify section exists
        ForumSectionEntity sectionEntity;
        try
        {
            var sectionResponse = await _sectionsTable.GetEntityAsync<ForumSectionEntity>("FORUM", sectionId);
            sectionEntity = sectionResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new KeyNotFoundException($"Section '{sectionId}' not found.");
        }

        // 2. Create topic
        var topicId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var authorAvatar = await GetAuthorAvatarAsync(authorId);

        var topicEntity = new ForumTopicEntity
        {
            PartitionKey = ForumTopicEntity.GetPartitionKey(sectionId),
            RowKey = topicId,
            SectionId = sectionId,
            EventId = string.Empty,
            Title = title,
            Content = content,
            AuthorId = authorId,
            AuthorName = authorName,
            AuthorAvatar = authorAvatar ?? string.Empty,
            IsPinned = false,
            IsLocked = false,
            ReplyCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
            NoviceVisible = noviceVisible,
            NoviceCanReply = noviceCanReply
        };
        await _topicsTable.AddEntityAsync(topicEntity);

        // 3. Create topic index entry
        var indexEntity = new ForumTopicIndexEntity
        {
            PartitionKey = "TOPICINDEX",
            RowKey = topicId,
            SectionId = sectionId
        };
        await _topicIndexTable.AddEntityAsync(indexEntity);

        // 4. Increment TopicCount on the section (read-merge-upsert)
        sectionEntity.TopicCount++;
        await _sectionsTable.UpdateEntityAsync(sectionEntity, sectionEntity.ETag, TableUpdateMode.Merge);

        // 5. Return DTO
        return new ForumTopicDto
        {
            Id = topicId,
            SectionId = sectionId,
            Title = title,
            Content = content,
            AuthorId = authorId,
            AuthorName = authorName,
            AuthorAvatar = null,
            IsPinned = false,
            IsLocked = false,
            ReplyCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
            NoviceVisible = noviceVisible ?? true,
            NoviceCanReply = noviceCanReply ?? true
        };
    }

    public async Task<ForumTopicDto?> UpdateTopicAsync(string topicId, UpdateTopicRequestDto update)
    {
        ForumTopicIndexEntity indexEntity;
        try
        {
            var indexResponse = await _topicIndexTable.GetEntityAsync<ForumTopicIndexEntity>("TOPICINDEX", topicId);
            indexEntity = indexResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        var pk = ForumTopicEntity.GetPartitionKey(indexEntity.SectionId);
        ForumTopicEntity topicEntity;
        try
        {
            var resp = await _topicsTable.GetEntityAsync<ForumTopicEntity>(pk, topicId);
            topicEntity = resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        if (update.NoviceVisible.HasValue) topicEntity.NoviceVisible = update.NoviceVisible.Value;
        if (update.NoviceCanReply.HasValue) topicEntity.NoviceCanReply = update.NoviceCanReply.Value;
        if (update.IsPinned.HasValue) topicEntity.IsPinned = update.IsPinned.Value;
        if (update.IsLocked.HasValue) topicEntity.IsLocked = update.IsLocked.Value;
        topicEntity.UpdatedAt = DateTime.UtcNow;

        await _topicsTable.UpdateEntityAsync(topicEntity, topicEntity.ETag);
        return ToTopicDto(topicEntity);
    }

    private static ForumReplyDto ToReplyDto(ForumReplyEntity entity, string? currentAvatar = null, UserDto? author = null) => new ForumReplyDto
    {
        Id = entity.ReplyId,
        TopicId = entity.TopicId,
        AuthorId = entity.AuthorId,
        AuthorName = entity.AuthorName,
        AuthorAvatar = string.IsNullOrEmpty(currentAvatar) ? null : currentAvatar,
        Content = entity.Content,
        CreatedAt = entity.CreatedAt,
        Likes = entity.Likes,
        ImageUrls = JsonSerializer.Deserialize<List<string>>(entity.ImageUrls ?? "[]") ?? new List<string>(),
        AuthorRank = author?.Rank ?? UserRank.Novice,
        AuthorStaffRole = author?.StaffRole ?? StaffRole.None
    };
}
