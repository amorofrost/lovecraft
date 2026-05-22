using Azure;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Workers;

public sealed class MetricsRollupWorker : BackgroundService
{
    private readonly TableServiceClient _tables;
    private readonly ILogger<MetricsRollupWorker> _logger;
    private static readonly string[] Categories = { "request_timing", "bi_events", "container_stats", "frontend_perf" };

    public MetricsRollupWorker(TableServiceClient tables, ILogger<MetricsRollupWorker> logger)
    {
        _tables = tables;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRunAt = NextTopOfHourPlus5(now);
            var delay = nextRunAt - now;
            try { await Task.Delay(delay, stoppingToken); } catch (TaskCanceledException) { return; }
            try
            {
                await RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MetricsRollupWorker run failed");
            }
        }
    }

    public static DateTime NextTopOfHourPlus5(DateTime now)
    {
        var topNext = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(1).AddMinutes(5);
        return topNext;
    }

    public async Task RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        var minute = _tables.GetTableClient(TableNames.MetricsMinute);
        var hour = _tables.GetTableClient(TableNames.MetricsHour);
        await minute.CreateIfNotExistsAsync(ct);
        await hour.CreateIfNotExistsAsync(ct);

        for (int hoursBack = 1; hoursBack <= 6; hoursBack++)
        {
            var target = nowUtc.AddHours(-hoursBack);
            foreach (var cat in Categories)
                await RollupHourAsync(minute, hour, target, cat, force: hoursBack == 1, ct);
        }
    }

    private async Task RollupHourAsync(TableClient minute, TableClient hour, DateTime hourUtc, string category, bool force, CancellationToken ct)
    {
        var pkMinute = $"{hourUtc:yyyy-MM-dd'T'HH}#{category}";
        var rows = new List<MetricMinuteEntity>();
        await foreach (var r in minute.QueryAsync<MetricMinuteEntity>(filter: $"PartitionKey eq '{pkMinute}'", cancellationToken: ct))
            rows.Add(r);
        if (rows.Count == 0) return;

        var pkHour = $"{hourUtc:yyyy-MM-dd}#{category}";
        foreach (var group in rows.GroupBy(r => DimensionFromRowKey(r.RowKey)))
        {
            var rk = $"{hourUtc:HH}#{group.Key}";
            if (!force)
            {
                try
                {
                    var existing = await hour.GetEntityAsync<MetricHourEntity>(pkHour, rk, cancellationToken: ct);
                    if (existing.Value.SourceMinuteRowCount == group.Count()) continue;
                }
                catch (RequestFailedException ex) when (ex.Status == 404) { /* not yet rolled up */ }
            }

            var agg = AggregateGroup(group.Key, group.ToList());
            agg.PartitionKey = pkHour;
            agg.RowKey = rk;
            await hour.UpsertEntityAsync(agg, TableUpdateMode.Replace, ct);
        }
    }

    private static string DimensionFromRowKey(string rowKey)
    {
        var idx = rowKey.IndexOf('#');
        return idx < 0 ? rowKey : rowKey[(idx + 1)..];
    }

    public static MetricHourEntity AggregateGroup(string dimensionKey, IReadOnlyList<MetricMinuteEntity> rows)
    {
        var h = new MetricHourEntity { SourceMinuteRowCount = rows.Count };
        foreach (var r in rows)
        {
            h.Count += r.Count;
            h.SumMs = (h.SumMs ?? 0) + (r.SumMs ?? 0);
            if (r.MinMs.HasValue) h.MinMs = h.MinMs.HasValue ? Math.Min(h.MinMs.Value, r.MinMs.Value) : r.MinMs;
            if (r.MaxMs.HasValue) h.MaxMs = h.MaxMs.HasValue ? Math.Max(h.MaxMs.Value, r.MaxMs.Value) : r.MaxMs;
            h.B0 = (h.B0 ?? 0) + (r.B0 ?? 0);
            h.B1 = (h.B1 ?? 0) + (r.B1 ?? 0);
            h.B2 = (h.B2 ?? 0) + (r.B2 ?? 0);
            h.B3 = (h.B3 ?? 0) + (r.B3 ?? 0);
            h.B4 = (h.B4 ?? 0) + (r.B4 ?? 0);
            h.B5 = (h.B5 ?? 0) + (r.B5 ?? 0);
            h.B6 = (h.B6 ?? 0) + (r.B6 ?? 0);
            h.B7 = (h.B7 ?? 0) + (r.B7 ?? 0);
            h.B8 = (h.B8 ?? 0) + (r.B8 ?? 0);
        }
        return h;
    }
}
