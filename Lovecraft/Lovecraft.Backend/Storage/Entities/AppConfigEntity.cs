using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class AppConfigEntity : ITableEntity
{
    // PK = partition name ("rank_thresholds" | "permissions" | "registration")
    // RK = config key (e.g. "active_replies", "create_topic", "require_event_invite")
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Value { get; set; } = string.Empty;

    public const string PartitionRankThresholds = "rank_thresholds";
    public const string PartitionPermissions = "permissions";
    public const string PartitionRegistration = "registration";
    public const string PartitionPagination = "pagination";
}
