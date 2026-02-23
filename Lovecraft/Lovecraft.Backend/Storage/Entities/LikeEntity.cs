using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class LikeEntity : ITableEntity
{
    // PK = fromUserId, RK = toUserId (for likes table)
    // PK = toUserId, RK = fromUserId (for likesreceived table)
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string LikeId { get; set; } = string.Empty;
    public string FromUserId { get; set; } = string.Empty;
    public string ToUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsMatch { get; set; }
}
