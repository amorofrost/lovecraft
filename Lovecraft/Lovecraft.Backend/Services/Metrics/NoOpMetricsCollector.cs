namespace Lovecraft.Backend.Services.Metrics;

public sealed class NoOpMetricsCollector : IMetricsCollector
{
    public MetricsEnabledFlags CurrentFlags { get; private set; } = MetricsEnabledFlags.AllDisabled;
    public void UpdateFlags(MetricsEnabledFlags flags) => CurrentFlags = flags;
    public void RecordTiming(string category, string dimensionKey, double ms) { }
    public void RecordCount(string category, string dimensionKey, long delta = 1) { }
    public Task RecordContainerStatusAsync(ContainerStatusSnapshot snapshot, CancellationToken ct = default) => Task.CompletedTask;
    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}
