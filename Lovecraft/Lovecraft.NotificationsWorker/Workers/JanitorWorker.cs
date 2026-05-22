using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Configuration;
using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Workers;

public class JanitorWorker : BackgroundService
{
    private static readonly TimeSpan DayInterval = TimeSpan.FromHours(24);
    private const int ScheduledHourUtc = 3;

    private readonly IOutboxJanitor _janitor;
    private readonly WorkerMetricsConfigReader _metricsConfig;
    private readonly TableServiceClient _tables;
    private readonly ILogger<JanitorWorker> _logger;

    public JanitorWorker(
        IOutboxJanitor janitor,
        WorkerMetricsConfigReader metricsConfig,
        TableServiceClient tables,
        ILogger<JanitorWorker> logger)
    {
        _janitor = janitor;
        _metricsConfig = metricsConfig;
        _tables = tables;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        var nextRun = new DateTime(now.Year, now.Month, now.Day, ScheduledHourUtc, 0, 0, DateTimeKind.Utc);
        if (nextRun <= now) nextRun = nextRun.AddDays(1);
        var initialDelay = nextRun - now;
        _logger.LogInformation("JanitorWorker starting; first run at {Next} (in {Delay})", nextRun, initialDelay);

        try { await Task.Delay(initialDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _janitor.RunAsync(DateTime.UtcNow, stoppingToken);
                var cfg = await _metricsConfig.GetAsync(stoppingToken);
                await CleanupMetricsTablesAsync(cfg.RetentionMinuteHours, cfg.RetentionHourDays, cfg.RetentionDauDays, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JanitorWorker run failed");
            }

            try { await Task.Delay(DayInterval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    // ── Metrics retention ────────────────────────────────────────────────────

    private async Task CleanupMetricsTablesAsync(int minuteHours, int hourDays, int dauDays, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        _logger.LogInformation(
            "JanitorWorker: starting metrics cleanup (minuteRetention={MinH}h, hourRetention={HourD}d, dauRetention={DauD}d)",
            minuteHours, hourDays, dauDays);

        await CleanupTablePartitionsAsync(TableNames.MetricsMinute, pk => ShouldDeleteMinutePartition(pk, now, minuteHours), ct);
        await CleanupTablePartitionsAsync(TableNames.MetricsHour,   pk => ShouldDeleteHourPartition(pk, now, hourDays), ct);
        await CleanupTablePartitionsAsync(TableNames.DailyActiveUsers, pk => ShouldDeleteDauPartition(pk, now, dauDays), ct);
    }

    private async Task CleanupTablePartitionsAsync(string tableName, Func<string, bool> shouldDelete, CancellationToken ct)
    {
        var table = _tables.GetTableClient(tableName);
        try { await table.CreateIfNotExistsAsync(ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JanitorWorker: could not ensure table {Table} exists; skipping", tableName);
            return;
        }

        var seenPartitions = new HashSet<string>();
        var deletedPartitions = 0;

        await foreach (var entity in table.QueryAsync<TableEntity>(
            select: new[] { "PartitionKey" }, cancellationToken: ct))
        {
            if (!seenPartitions.Add(entity.PartitionKey)) continue;
            if (!shouldDelete(entity.PartitionKey)) continue;

            await DeletePartitionAsync(table, entity.PartitionKey, ct);
            deletedPartitions++;
        }

        _logger.LogInformation("JanitorWorker: cleaned {Count} old partition(s) from {Table}", deletedPartitions, tableName);
    }

    private async Task DeletePartitionAsync(TableClient table, string pk, CancellationToken ct)
    {
        var batch = new List<TableTransactionAction>();
        await foreach (var entity in table.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{pk}'",
            select: new[] { "PartitionKey", "RowKey" },
            cancellationToken: ct))
        {
            batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity));
            if (batch.Count == 100)
            {
                await table.SubmitTransactionAsync(batch, ct);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
            await table.SubmitTransactionAsync(batch, ct);
    }

    // ── Public static helpers (tested directly) ──────────────────────────────

    /// <summary>
    /// Returns true if a <c>metricsminute</c> partition key (format <c>yyyy-MM-dd'T'HH#category</c>)
    /// represents an hour older than <paramref name="retentionHours"/> ago.
    /// </summary>
    public static bool ShouldDeleteMinutePartition(string pk, DateTime nowUtc, int retentionHours)
    {
        var hashIdx = pk.IndexOf('#');
        if (hashIdx < 0) return false;
        if (!DateTime.TryParseExact(
                pk[..hashIdx],
                "yyyy-MM-dd'T'HH",
                null,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var ts))
            return false;
        return ts < nowUtc.AddHours(-retentionHours);
    }

    /// <summary>
    /// Returns true if a <c>metricshour</c> partition key (format <c>yyyy-MM-dd#category</c>)
    /// represents a date older than <paramref name="retentionDays"/> days ago.
    /// </summary>
    public static bool ShouldDeleteHourPartition(string pk, DateTime nowUtc, int retentionDays)
    {
        var hashIdx = pk.IndexOf('#');
        if (hashIdx < 0) return false;
        if (!DateTime.TryParseExact(
                pk[..hashIdx],
                "yyyy-MM-dd",
                null,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var d))
            return false;
        return d < nowUtc.Date.AddDays(-retentionDays);
    }

    /// <summary>
    /// Returns true if a <c>dailyactiveusers</c> partition key (format <c>yyyy-MM-dd</c>)
    /// represents a date older than <paramref name="retentionDays"/> + 1 days ago.
    /// The extra day ensures the most-recent complete day is always retained.
    /// </summary>
    public static bool ShouldDeleteDauPartition(string pk, DateTime nowUtc, int retentionDays)
    {
        if (!DateTime.TryParseExact(
                pk,
                "yyyy-MM-dd",
                null,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var d))
            return false;
        return d < nowUtc.Date.AddDays(-retentionDays - 1);
    }
}
