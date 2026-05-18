using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

/// <summary>
/// One row per (notification, channel) delivery attempt.
/// PartitionKey = OUTBOX_{channel}_PENDING while pending,
/// OUTBOX_{channel}_DONE_{yyyy-MM-dd} after success,
/// OUTBOX_{channel}_DEAD_{yyyy-MM-dd} after 5 failed attempts.
/// RowKey = {scheduledForUtc:yyyy-MM-ddTHH:mm:ss}_{notificationId} (lex = chronological).
/// </summary>
public class NotificationOutboxEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string NotificationId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public DateTime ScheduledForUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }

    public static string PendingPartition(string channel) => $"OUTBOX_{channel}_PENDING";
    public static string DonePartition(string channel, DateTime utc) =>
        $"OUTBOX_{channel}_DONE_{utc:yyyy-MM-dd}";
    public static string DeadPartition(string channel, DateTime utc) =>
        $"OUTBOX_{channel}_DEAD_{utc:yyyy-MM-dd}";
    public static string GetRowKey(DateTime scheduledForUtc, string notificationId) =>
        $"{scheduledForUtc:yyyy-MM-ddTHH:mm:ss}_{notificationId}";
}
