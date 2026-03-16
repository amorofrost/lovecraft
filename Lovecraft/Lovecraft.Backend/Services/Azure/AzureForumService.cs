using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Forum;

namespace Lovecraft.Backend.Services.Azure;

public class AzureForumService : IForumService
{
    private readonly TableClient _sectionsTable;
    private readonly TableClient _topicsTable;
    private readonly TableClient _topicIndexTable;
    private readonly TableClient _repliesTable;
    private readonly ILogger<AzureForumService> _logger;

    public AzureForumService(TableServiceClient tableServiceClient, ILogger<AzureForumService> logger)
    {
        _logger = logger;
        _sectionsTable = tableServiceClient.GetTableClient(TableNames.ForumSections);
        _topicsTable = tableServiceClient.GetTableClient(TableNames.ForumTopics);
        _topicIndexTable = tableServiceClient.GetTableClient(TableNames.ForumTopicIndex);
        _repliesTable = tableServiceClient.GetTableClient(TableNames.ForumReplies);

        Task.WhenAll(
            _sectionsTable.CreateIfNotExistsAsync(),
            _topicsTable.CreateIfNotExistsAsync(),
            _topicIndexTable.CreateIfNotExistsAsync(),
            _repliesTable.CreateIfNotExistsAsync()
        ).GetAwaiter().GetResult();
    }

    public async Task<List<ForumSectionDto>> GetSectionsAsync()
    {
        var results = new List<ForumSectionDto>();
        await foreach (var entity in _sectionsTable.QueryAsync<ForumSectionEntity>(filter: "PartitionKey eq 'FORUM'"))
        {
            results.Add(ToSectionDto(entity));
        }
        return results.OrderBy(s => s.Id).ToList();
    }

    public async Task<List<ForumTopicDto>> GetTopicsAsync(string sectionId)
    {
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
        var results = new List<ForumReplyDto>();
        // Results come back newest-first due to reversed-ticks RK ordering
        await foreach (var entity in _repliesTable.QueryAsync<ForumReplyEntity>(
            filter: $"PartitionKey eq '{pk}'"))
        {
            results.Add(ToReplyDto(entity));
        }
        return results;
    }

    public async Task<ForumReplyDto> CreateReplyAsync(string topicId, string authorId, string authorName, string content)
    {
        var now = DateTime.UtcNow;
        var replyId = Guid.NewGuid().ToString();

        var replyEntity = new ForumReplyEntity
        {
            PartitionKey = ForumReplyEntity.GetPartitionKey(topicId),
            RowKey = ForumReplyEntity.BuildRowKey(now, replyId),
            ReplyId = replyId,
            TopicId = topicId,
            AuthorId = authorId,
            AuthorName = authorName,
            Content = content,
            CreatedAt = now,
            Likes = 0
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

        return ToReplyDto(replyEntity);
    }

    private static ForumSectionDto ToSectionDto(ForumSectionEntity entity) => new ForumSectionDto
    {
        Id = entity.RowKey,
        Name = entity.Name,
        Description = entity.Description,
        TopicCount = entity.TopicCount
    };

    private static ForumTopicDto ToTopicDto(ForumTopicEntity entity) => new ForumTopicDto
    {
        Id = entity.RowKey,
        SectionId = entity.SectionId,
        Title = entity.Title,
        Content = entity.Content,
        AuthorId = entity.AuthorId,
        AuthorName = entity.AuthorName,
        AuthorAvatar = string.IsNullOrEmpty(entity.AuthorAvatar) ? null : entity.AuthorAvatar,
        IsPinned = entity.IsPinned,
        IsLocked = entity.IsLocked,
        ReplyCount = entity.ReplyCount,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
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

    public Task<ForumTopicDto> CreateTopicAsync(
        string sectionId, string authorId, string authorName, string title, string content)
    {
        throw new NotImplementedException();
    }

    private static ForumReplyDto ToReplyDto(ForumReplyEntity entity) => new ForumReplyDto
    {
        Id = entity.ReplyId,
        TopicId = entity.TopicId,
        AuthorId = entity.AuthorId,
        AuthorName = entity.AuthorName,
        AuthorAvatar = string.IsNullOrEmpty(entity.AuthorAvatar) ? null : entity.AuthorAvatar,
        Content = entity.Content,
        CreatedAt = entity.CreatedAt,
        Likes = entity.Likes
    };
}
