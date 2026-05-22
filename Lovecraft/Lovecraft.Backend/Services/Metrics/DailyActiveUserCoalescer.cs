using System.Collections.Concurrent;
using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;

namespace Lovecraft.Backend.Services.Metrics;

public sealed class DailyActiveUserCoalescer
{
    private readonly ConcurrentDictionary<string, DateTime> _lastFlushedByUserDay = new();
    private readonly int _windowSeconds;
    private readonly TableServiceClient? _tableService;
    // Not volatile — CreateIfNotExistsAsync is idempotent so redundant calls under race are safe.
    private bool _tableInitialized;

    public DailyActiveUserCoalescer(int windowSeconds = 60, TableServiceClient? tableService = null)
    {
        _windowSeconds = windowSeconds;
        _tableService = tableService;
    }

    /// <summary>
    /// Returns true if a flush should happen for this (userId, today UTC).
    /// Side effect: calling this method records the flush time. Do NOT call speculatively
    /// before <see cref="FlushIfNeededAsync"/> — the second call will always return false.
    /// </summary>
    public bool ShouldFlush(string userId, DateTime nowUtc)
    {
        // Race window: two concurrent callers for the same (user, date) can both return true,
        // causing one extra Azure write per race. Acceptable trade-off vs locking the hot path
        // (DAU is approximate; duplicate writes are idempotent — RequestCount may under-count
        // by 1 on contention, never double-count).
        var key = $"{nowUtc:yyyy-MM-dd}#{userId}";
        if (!_lastFlushedByUserDay.TryGetValue(key, out var last))
        {
            _lastFlushedByUserDay[key] = nowUtc;
            return true;
        }
        if ((nowUtc - last).TotalSeconds >= _windowSeconds)
        {
            _lastFlushedByUserDay[key] = nowUtc;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Calls <see cref="ShouldFlush"/> and, if true, upserts a <see cref="DailyActiveUserEntity"/>
    /// row in Azure Table Storage. No-ops when no <see cref="TableServiceClient"/> is configured.
    /// </summary>
    public async Task FlushIfNeededAsync(string userId, DateTime nowUtc, CancellationToken ct)
    {
        if (_tableService is null || !ShouldFlush(userId, nowUtc)) return;

        var table = _tableService.GetTableClient(TableNames.DailyActiveUsers);
        if (!_tableInitialized)
        {
            await table.CreateIfNotExistsAsync(ct);
            _tableInitialized = true;
        }

        var pk = nowUtc.ToString("yyyy-MM-dd");
        DailyActiveUserEntity? existing = null;
        try
        {
            existing = (await table.GetEntityAsync<DailyActiveUserEntity>(pk, userId, cancellationToken: ct)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        var entity = existing ?? new DailyActiveUserEntity
        {
            PartitionKey = pk,
            RowKey = userId,
            FirstSeenUtc = nowUtc,
        };
        entity.LastSeenUtc = nowUtc;
        entity.RequestCount += 1;
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }
}
