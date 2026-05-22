using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class MetricMinuteEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;  // "{yyyy-MM-ddTHH}#{category}"
    public string RowKey { get; set; } = string.Empty;        // "{mm}#{dimensionKey}"
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public long Count { get; set; }
    public long? SumMs { get; set; }
    public long? MinMs { get; set; }
    public long? MaxMs { get; set; }
    // Histogram buckets B0..B8; boundaries defined in HistogramBuckets.cs (added in Task 2).
    public long? B0 { get; set; }
    public long? B1 { get; set; }
    public long? B2 { get; set; }
    public long? B3 { get; set; }
    public long? B4 { get; set; }
    public long? B5 { get; set; }
    public long? B6 { get; set; }
    public long? B7 { get; set; }
    public long? B8 { get; set; }
    public string LabelsJson { get; set; } = "{}";
}
