using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class ForumSectionEntity : ITableEntity
{
    // PK = "FORUM", RK = sectionId
    public string PartitionKey { get; set; } = "FORUM";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TopicCount { get; set; }
    public int OrderIndex { get; set; }
}
