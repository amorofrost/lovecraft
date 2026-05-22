using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Services.Metrics;

public sealed class ContainerHeartbeatWorker : BackgroundService
{
    private readonly IMetricsCollector _collector;
    private readonly ILogger<ContainerHeartbeatWorker> _logger;
    private readonly string _containerName;
    private readonly string _version;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    public ContainerHeartbeatWorker(IMetricsCollector collector, ILogger<ContainerHeartbeatWorker> logger,
                                    string containerName, string version)
    {
        _collector = collector;
        _logger = logger;
        _containerName = containerName;
        _version = version;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snap = CaptureSnapshot(_containerName, _startedAtUtc, _version);
                await _collector.RecordContainerStatusAsync(snap, stoppingToken);
                _collector.RecordTiming("container_stats", $"{_containerName}|working_set_mb", snap.WorkingSetMb ?? 0);
                _collector.RecordTiming("container_stats", $"{_containerName}|gc_heap_mb", snap.GcHeapMb ?? 0);
                _collector.RecordTiming("container_stats", $"{_containerName}|thread_count", snap.ThreadCount ?? 0);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Heartbeat failed for {Container}", _containerName); }
            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    public static ContainerStatusSnapshot CaptureSnapshot(string name, DateTime startedAt, string version)
    {
        using var proc = Process.GetCurrentProcess();
        return new ContainerStatusSnapshot(
            Name: name,
            LastHeartbeatUtc: DateTime.UtcNow,
            StartedAtUtc: startedAt,
            Version: version,
            GcHeapMb: GC.GetTotalMemory(false) / (1024 * 1024),
            WorkingSetMb: proc.WorkingSet64 / (1024 * 1024),
            ThreadCount: proc.Threads.Count,
            CpuSecondsTotal: proc.TotalProcessorTime.TotalSeconds,
            RequestsServed: null,
            Note: null);
    }
}
