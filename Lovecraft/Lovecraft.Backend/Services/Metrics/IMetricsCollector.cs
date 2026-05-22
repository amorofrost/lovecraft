namespace Lovecraft.Backend.Services.Metrics;

public interface IMetricsCollector
{
    MetricsEnabledFlags CurrentFlags { get; }
    void UpdateFlags(MetricsEnabledFlags flags);
    void RecordTiming(string category, string dimensionKey, double ms);
    void RecordCount(string category, string dimensionKey, long delta = 1);
    Task RecordContainerStatusAsync(ContainerStatusSnapshot snapshot, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);
}
