using System.Diagnostics;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Workers;

public sealed class ContainerHeartbeatWorker : BackgroundService
{
    private readonly TableServiceClient _tables;
    private readonly ILogger<ContainerHeartbeatWorker> _logger;
    private readonly string _name;
    private readonly string _version;
    private readonly ContainerMetricsReporter? _reporter;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private bool _tableInit;
    private double? _lastCpuSeconds;
    private DateTime? _lastSampleUtc;

    public ContainerHeartbeatWorker(TableServiceClient tables, ILogger<ContainerHeartbeatWorker> logger,
                                    string name, ContainerMetricsReporter? reporter = null)
    {
        _tables = tables;
        _logger = logger;
        _name = name;
        _reporter = reporter;
        _version = typeof(ContainerHeartbeatWorker).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    // Pure CPU% helper duplicated from Lovecraft.Backend.Services.Metrics.ContainerCpuMath. Keep in sync.
    private static double? ComputeCpuPercent(double cpuNow, double? cpuPrev, double elapsedSeconds, int cores)
    {
        if (cpuPrev is null || elapsedSeconds <= 0 || cores <= 0) return null;
        return Math.Clamp((cpuNow - cpuPrev.Value) / (elapsedSeconds * cores) * 100.0, 0.0, 100.0);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var table = _tables.GetTableClient(TableNames.ContainerStatus);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_tableInit) { await table.CreateIfNotExistsAsync(ct); _tableInit = true; }
                using var proc = Process.GetCurrentProcess();
                var now = DateTime.UtcNow;
                var heap = GC.GetTotalMemory(false) / (1024 * 1024);
                var ws = proc.WorkingSet64 / (1024 * 1024);
                var threads = proc.Threads.Count;
                var cpuSeconds = proc.TotalProcessorTime.TotalSeconds;

                var elapsed = _lastSampleUtc is null ? 0 : (now - _lastSampleUtc.Value).TotalSeconds;
                var cpuPercent = ComputeCpuPercent(cpuSeconds, _lastCpuSeconds, elapsed, Environment.ProcessorCount);
                _lastCpuSeconds = cpuSeconds;
                _lastSampleUtc = now;

                var entity = new ContainerStatusEntity
                {
                    PartitionKey = "STATUS",
                    RowKey = _name,
                    LastHeartbeatUtc = now,
                    StartedAtUtc = _startedAt,
                    Version = _version,
                    WorkingSetMb = ws,
                    GcHeapMb = heap,
                    ThreadCount = threads,
                    CpuSecondsTotal = cpuSeconds,
                    CpuPercent = cpuPercent,
                };
                await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

                if (_reporter is not null)
                    await _reporter.ReportAsync(_name, heap, ws, threads, cpuPercent, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Heartbeat failed"); }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}
