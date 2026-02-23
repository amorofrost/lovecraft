using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class ForumTopicIndexEntity : ITableEntity
{
    // PK = "TOPICINDEX", RK = topicId
    public string PartitionKey { get; set; } = "TOPICINDEX";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string SectionId { get; set; } = string.Empty;
}
