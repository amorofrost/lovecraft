using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

/// <summary>
/// Represents a user's explicit subscription to a forum topic for notification purposes.
/// PartitionKey = topicId so listing subscribers of a topic is a single partition scan;
/// RowKey = userId so "is user X subscribed to topic Y" is a point-read.
/// </summary>
public class ForumTopicSubscriptionEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string TopicId { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime SubscribedAtUtc { get; set; }

    /// <summary>"create" | "reply" | "manual" — how this subscription was created.</summary>
    public string Source { get; set; } = "manual";
}
