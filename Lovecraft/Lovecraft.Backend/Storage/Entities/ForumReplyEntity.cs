using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class ForumReplyEntity : ITableEntity
{
    // PK = "topic#{topicId}", RK = {reversedTicks}#{replyId}
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ReplyId { get; set; } = string.Empty;
    public string TopicId { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorAvatar { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int Likes { get; set; }
    public string ImageUrls { get; set; } = "[]"; // stored as JSON array

    public static string GetPartitionKey(string topicId) => $"topic-{topicId}";

    public static string BuildRowKey(DateTime createdAt, string replyId)
    {
        var reversedTicks = (DateTime.MaxValue.Ticks - createdAt.Ticks).ToString("D19");
        return $"{reversedTicks}-{replyId}";
    }
}
