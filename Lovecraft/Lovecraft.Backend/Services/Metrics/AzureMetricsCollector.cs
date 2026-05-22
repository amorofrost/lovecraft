using System.Collections.Concurrent;
using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;

namespace Lovecraft.Backend.Services.Metrics;

public sealed class AzureMetricsCollector : IMetricsCollector
{
    private readonly ConcurrentQueue<MetricSample> _buffer = new();
    private readonly int _capacity;
    private readonly TableServiceClient? _tableService;
    private readonly ContainerStatusUpserter? _statusUpserter;

    public AzureMetricsCollector(int capacity = 1000, TableServiceClient? tableService = null)
    {
        _capacity = capacity;
        _tableService = tableService;
        if (tableService is not null)
            _statusUpserter = new ContainerStatusUpserter(tableService.GetTableClient(TableNames.ContainerStatus));
    }

    public MetricsEnabledFlags CurrentFlags { get; private set; } = MetricsEnabledFlags.AllEnabled;
    public void UpdateFlags(MetricsEnabledFlags flags) => CurrentFlags = flags;

    public int PendingCount => _buffer.Count;

    public void RecordTiming(string category, string dimensionKey, double ms)
    {
        if (!CurrentFlags.IsEnabled(category)) return;
        Enqueue(new MetricSample(category, dimensionKey, 1, ms, DateTime.UtcNow));
    }

    public void RecordCount(string category, string dimensionKey, long delta = 1)
    {
        if (!CurrentFlags.IsEnabled(category)) return;
        Enqueue(new MetricSample(category, dimensionKey, delta, null, DateTime.UtcNow));
    }

    private void Enqueue(MetricSample s)
    {
        _buffer.Enqueue(s);
        // Soft cap: under burst contention this can drop slightly more than (count - capacity)
        // because the Count check is racy. Acceptable — we'd rather lose a few stale samples
        // than block the hot path with a lock.
        while (_buffer.Count > _capacity && _buffer.TryDequeue(out _)) { }
    }

    public async Task RecordContainerStatusAsync(ContainerStatusSnapshot snapshot, CancellationToken ct = default)
    {
        if (!CurrentFlags.IsEnabled("container_stats")) return;
        if (_statusUpserter is null) return;
        await _statusUpserter.UpsertAsync(snapshot, ct);
    }

    public IReadOnlyList<MetricMinuteAggregate> DrainBatchForFlush()
    {
        var samples = new List<MetricSample>();
        while (_buffer.TryDequeue(out var s)) samples.Add(s);

        return samples
            .GroupBy(s => (
                Pk: $"{s.CapturedAtUtc:yyyy-MM-dd'T'HH}#{s.Category}",
                Rk: $"{s.CapturedAtUtc:mm}#{s.DimensionKey}"))
            .Select(g => Aggregate(g.Key.Pk, g.Key.Rk, g.ToList()))
            .ToList();
    }

    private static MetricMinuteAggregate Aggregate(string pk, string rk, IReadOnlyList<MetricSample> samples)
    {
        long count = 0, sumMs = 0;
        long? min = null, max = null;
        var b = HistogramBuckets.Empty();
        foreach (var s in samples)
        {
            count += s.Count;
            if (s.DurationMs.HasValue)
            {
                var ms = (long)s.DurationMs.Value;
                sumMs += ms;
                min = min is null ? ms : Math.Min(min.Value, ms);
                max = max is null ? ms : Math.Max(max.Value, ms);
                b[HistogramBuckets.IndexFor(ms)]++;
            }
        }
        return new MetricMinuteAggregate(pk, rk, count, sumMs, min, max, b);
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_tableService is null) return;
        var batch = DrainBatchForFlush();
        if (batch.Count == 0) return;
        var table = _tableService.GetTableClient(TableNames.MetricsMinute);
        await table.CreateIfNotExistsAsync(ct);
        foreach (var agg in batch)
            await UpsertWithRetry(table, agg, ct);
    }

    private static async Task UpsertWithRetry(TableClient table, MetricMinuteAggregate agg, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await MergeAsync(table, agg, ct);
                return;
            }
            catch (RequestFailedException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)), ct);
            }
        }
    }

    private static async Task MergeAsync(TableClient table, MetricMinuteAggregate agg, CancellationToken ct)
    {
        MetricMinuteEntity? existing = null;
        try
        {
            existing = (await table.GetEntityAsync<MetricMinuteEntity>(agg.PartitionKey, agg.RowKey, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        var entity = existing ?? new MetricMinuteEntity { PartitionKey = agg.PartitionKey, RowKey = agg.RowKey };
        entity.Count += agg.Count;
        entity.SumMs = (entity.SumMs ?? 0) + (agg.SumMs ?? 0);
        if (agg.MinMs.HasValue) entity.MinMs = entity.MinMs.HasValue ? Math.Min(entity.MinMs.Value, agg.MinMs.Value) : agg.MinMs;
        if (agg.MaxMs.HasValue) entity.MaxMs = entity.MaxMs.HasValue ? Math.Max(entity.MaxMs.Value, agg.MaxMs.Value) : agg.MaxMs;
        entity.B0 = (entity.B0 ?? 0) + agg.Buckets[0];
        entity.B1 = (entity.B1 ?? 0) + agg.Buckets[1];
        entity.B2 = (entity.B2 ?? 0) + agg.Buckets[2];
        entity.B3 = (entity.B3 ?? 0) + agg.Buckets[3];
        entity.B4 = (entity.B4 ?? 0) + agg.Buckets[4];
        entity.B5 = (entity.B5 ?? 0) + agg.Buckets[5];
        entity.B6 = (entity.B6 ?? 0) + agg.Buckets[6];
        entity.B7 = (entity.B7 ?? 0) + agg.Buckets[7];
        entity.B8 = (entity.B8 ?? 0) + agg.Buckets[8];

        if (existing is null)
            await table.AddEntityAsync(entity, ct);
        else
            await table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge, ct);
    }
}

public sealed record MetricMinuteAggregate(
    string PartitionKey,
    string RowKey,
    long Count,
    long? SumMs,
    long? MinMs,
    long? MaxMs,
    long[] Buckets);

internal sealed class ContainerStatusUpserter
{
    private readonly TableClient _table;
    private bool _initialized;
    public ContainerStatusUpserter(TableClient table) { _table = table; }

    public async Task UpsertAsync(ContainerStatusSnapshot s, CancellationToken ct)
    {
        if (!_initialized)
        {
            await _table.CreateIfNotExistsAsync(ct);
            _initialized = true;
        }
        var entity = new ContainerStatusEntity
        {
            PartitionKey = "STATUS",
            RowKey = s.Name,
            LastHeartbeatUtc = s.LastHeartbeatUtc,
            StartedAtUtc = s.StartedAtUtc,
            Version = s.Version,
            GcHeapMb = s.GcHeapMb,
            WorkingSetMb = s.WorkingSetMb,
            ThreadCount = s.ThreadCount,
            CpuSecondsTotal = s.CpuSecondsTotal,
            RequestsServed = s.RequestsServed,
            Note = s.Note,
        };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }
}
