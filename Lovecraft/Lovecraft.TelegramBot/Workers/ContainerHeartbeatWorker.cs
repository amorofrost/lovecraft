using System.Diagnostics;
using Azure.Data.Tables;
using Lovecraft.TelegramBot.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.TelegramBot.Workers;

public sealed class ContainerHeartbeatWorker : BackgroundService
{
    private readonly TableServiceClient _tables;
    private readonly ILogger<ContainerHeartbeatWorker> _logger;
    private readonly string _name;
    private readonly string _version;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private bool _tableInit;

    public ContainerHeartbeatWorker(TableServiceClient tables, ILogger<ContainerHeartbeatWorker> logger, string name)
    {
        _tables = tables;
        _logger = logger;
        _name = name;
        _version = typeof(ContainerHeartbeatWorker).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var table = _tables.GetTableClient("containerstatus");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_tableInit) { await table.CreateIfNotExistsAsync(ct); _tableInit = true; }
                using var proc = Process.GetCurrentProcess();
                var entity = new ContainerStatusEntity
                {
                    PartitionKey = "STATUS",
                    RowKey = _name,
                    LastHeartbeatUtc = DateTime.UtcNow,
                    StartedAtUtc = _startedAt,
                    Version = _version,
                    WorkingSetMb = proc.WorkingSet64 / (1024 * 1024),
                    GcHeapMb = GC.GetTotalMemory(false) / (1024 * 1024),
                    ThreadCount = proc.Threads.Count,
                    CpuSecondsTotal = proc.TotalProcessorTime.TotalSeconds,
                };
                await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Heartbeat failed"); }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}
