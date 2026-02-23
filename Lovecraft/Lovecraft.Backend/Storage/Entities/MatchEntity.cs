using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class MatchEntity : ITableEntity
{
    // PK = userId, RK = matchId
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string MatchId { get; set; } = string.Empty;
    public string OtherUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string ChatId { get; set; } = string.Empty;
}
