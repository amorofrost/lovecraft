namespace Lovecraft.Common.DTOs.Metrics;

public sealed class ContainerStatsIngestDto
{
    public string Container { get; set; } = string.Empty;
    public long? GcHeapMb { get; set; }
    public long? WorkingSetMb { get; set; }
    public int? ThreadCount { get; set; }
    public double? CpuPercent { get; set; }
}
