using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

/// <summary>PartitionKey = userId, RowKey = INDEX.</summary>
public class NotificationPreferencesEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = "INDEX";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string MatrixJson { get; set; } = "{}";
    public string FrequencyJson { get; set; } = "{}";
    public int DailyDigestHourUtc { get; set; } = 9;
    public bool Mute { get; set; }
    public DateTime? MutedUntilUtc { get; set; }
}
