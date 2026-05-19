using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class BroadcastEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "BROADCAST";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Link { get; set; }
    public string AudienceJson { get; set; } = "{}";
    public string IssuedByUserId { get; set; } = "";
    public DateTime IssuedAtUtc { get; set; }
    public int EstimatedRecipients { get; set; }
    public int DispatchedCount { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime? CompletedAtUtc { get; set; }

    public static string BuildRowKey(DateTime issuedAtUtc, string id) =>
        $"{(DateTime.MaxValue.Ticks - issuedAtUtc.Ticks):D20}_{id}";
}
