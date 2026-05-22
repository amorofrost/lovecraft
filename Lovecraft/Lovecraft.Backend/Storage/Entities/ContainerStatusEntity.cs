using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class ContainerStatusEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "STATUS";
    public string RowKey { get; set; } = string.Empty;  // "backend" | "telegram-bot" | "notifications-worker" | "frontend"
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public DateTime LastHeartbeatUtc { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public string Version { get; set; } = string.Empty;
    public long? GcHeapMb { get; set; }
    public long? WorkingSetMb { get; set; }
    public int? ThreadCount { get; set; }
    public double? CpuSecondsTotal { get; set; }
    public long? RequestsServed { get; set; }
    public string? Note { get; set; }
}
