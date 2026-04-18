using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

/// <summary>
/// One row per issued invite code. <see cref="RowKey"/> is a deterministic hash of the plaintext code (server-side pepper).
/// </summary>
public class EventInviteEntity : ITableEntity
{
    public const string PartitionValue = "INVITE";

    public string PartitionKey { get; set; } = PartitionValue;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string EventId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public bool Revoked { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
