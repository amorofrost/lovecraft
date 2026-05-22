using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class DailyActiveUserEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;  // "{yyyy-MM-dd}"
    public string RowKey { get; set; } = string.Empty;        // userId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public long RequestCount { get; set; }
}
