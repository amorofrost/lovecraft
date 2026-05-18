using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

/// <summary>
/// Canonical notification record. PartitionKey = recipient userId,
/// RowKey = {invertedTicks}_{notificationId} so newest sort first.
/// </summary>
public class NotificationEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string NotificationId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
    /// <summary>Denormalized boolean for cheap OData filtering (Azure Table Storage does not support eq null on DateTime? columns).</summary>
    public bool IsRead { get; set; } = false;
    /// <summary>Denormalized boolean for cheap OData filtering (Azure Table Storage does not support eq null on DateTime? columns).</summary>
    public bool IsDismissed { get; set; } = false;
    public string? DigestGroupId { get; set; }
    /// <summary>Natural key of the underlying event (messageId / replyId / likeId) used by NotificationDeduper.</summary>
    public string? SourceEventId { get; set; }

    public static string GetPartitionKey(string userId) => userId;
    public static string GetRowKey(string notificationId, DateTime createdAtUtc) =>
        $"{(DateTime.MaxValue.Ticks - createdAtUtc.Ticks):D19}_{notificationId}";
}
