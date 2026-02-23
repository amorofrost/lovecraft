using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class RefreshTokenEntity : ITableEntity
{
    // PK = SHA256(token) hex, RK = "TOKEN"
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = "TOKEN";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string UserId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRevoked { get; set; }
}
