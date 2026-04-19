using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class ForumTopicEntity : ITableEntity
{
    // PK = "section#{sectionId}", RK = topicId
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string SectionId { get; set; } = string.Empty;

    /// <summary>When <see cref="SectionId"/> is <c>events</c>, matches <see cref="EventEntity.RowKey"/>.</summary>
    public string EventId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorAvatar { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public bool IsLocked { get; set; }
    public int ReplyCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string MinRank { get; set; } = "novice";
    public bool? NoviceVisible { get; set; }
    public bool? NoviceCanReply { get; set; }

    /// <summary>Stored values: <c>public</c>, <c>attendeesOnly</c>, <c>specificUsers</c>.</summary>
    public string EventTopicVisibility { get; set; } = "public";

    public string AllowedUserIdsJson { get; set; } = "[]";

    public static string GetPartitionKey(string sectionId) => $"section-{sectionId}";
}
