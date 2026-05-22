using System.Collections.Concurrent;

namespace Lovecraft.Backend.Services.Metrics;

public sealed class MockMetricsCollector : IMetricsCollector
{
    private readonly ConcurrentDictionary<(string Cat, string Dim), MetricBucket> _data = new();
    private readonly ConcurrentDictionary<string, ContainerStatusSnapshot> _containers = new();

    public MetricsEnabledFlags CurrentFlags { get; private set; } = MetricsEnabledFlags.AllEnabled;
    public void UpdateFlags(MetricsEnabledFlags flags) => CurrentFlags = flags;

    public void RecordTiming(string category, string dimensionKey, double ms)
    {
        if (!CurrentFlags.IsEnabled(category)) return;
        var bucket = _data.GetOrAdd((category, dimensionKey), _ => new MetricBucket());
        bucket.AddSample((long)ms);
    }

    public void RecordCount(string category, string dimensionKey, long delta = 1)
    {
        if (!CurrentFlags.IsEnabled(category)) return;
        var bucket = _data.GetOrAdd((category, dimensionKey), _ => new MetricBucket());
        bucket.AddCount(delta);
    }

    public Task RecordContainerStatusAsync(ContainerStatusSnapshot snapshot, CancellationToken ct = default)
    {
        if (!CurrentFlags.IsEnabled("container_stats")) return Task.CompletedTask;
        _containers[snapshot.Name] = snapshot;
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

    public IReadOnlyList<MetricRow> Snapshot() =>
        _data.Select(kv => new MetricRow(kv.Key.Cat, kv.Key.Dim, kv.Value)).ToList();

    public ContainerStatusSnapshot? GetContainerStatus(string name) =>
        _containers.TryGetValue(name, out var v) ? v : null;

    public void Reset()
    {
        _data.Clear();
        _containers.Clear();
    }
}

public sealed class MetricBucket
{
    private long _count;
    private long _sumMs;
    private long? _minMs;
    private long? _maxMs;
    private readonly long[] _buckets = HistogramBuckets.Empty();
    private readonly object _lock = new();

    public void AddSample(long ms)
    {
        lock (_lock)
        {
            _count++;
            _sumMs += ms;
            _minMs = _minMs is null ? ms : Math.Min(_minMs.Value, ms);
            _maxMs = _maxMs is null ? ms : Math.Max(_maxMs.Value, ms);
            _buckets[HistogramBuckets.IndexFor(ms)]++;
        }
    }

    public void AddCount(long delta)
    {
        lock (_lock) { _count += delta; }
    }

    public long Count { get { lock (_lock) return _count; } }
    public long SumMs { get { lock (_lock) return _sumMs; } }
    public long? MinMs { get { lock (_lock) return _minMs; } }
    public long? MaxMs { get { lock (_lock) return _maxMs; } }
    public long[] Buckets { get { lock (_lock) return (long[])_buckets.Clone(); } }
}

public sealed record MetricRow(string Category, string DimensionKey, MetricBucket Bucket)
{
    public long Count => Bucket.Count;
    public long SumMs => Bucket.SumMs;
    public long? MinMs => Bucket.MinMs;
    public long? MaxMs => Bucket.MaxMs;
    public long[] Buckets => Bucket.Buckets;
}
