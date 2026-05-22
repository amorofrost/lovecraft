using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Configuration;

/// <summary>
/// Reads metrics retention config from the shared <c>appconfig</c> Azure Table.
/// Worker-local equivalent of <c>AzureAppConfigService.GetMetricsConfigAsync</c>.
/// Partition: "metrics"; RowKeys match backend's <c>AppConfigKeys.MetricsKeys</c>.
/// </summary>
public sealed class WorkerMetricsConfigReader
{
    private const string Partition = "metrics";
    private const string KeyRetentionMinuteHours = "retention_minute_hours";
    private const string KeyRetentionHourDays = "retention_hour_days";
    private const string KeyRetentionDauDays = "retention_dau_days";

    private readonly TableClient _table;
    private readonly ILogger<WorkerMetricsConfigReader> _logger;

    public WorkerMetricsConfigReader(TableClient table, ILogger<WorkerMetricsConfigReader> logger)
    {
        _table = table;
        _logger = logger;
    }

    public async Task<WorkerMetricsConfig> GetAsync(CancellationToken ct = default)
    {
        var defaults = WorkerMetricsConfig.Defaults;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await foreach (var row in _table.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{Partition}'",
                select: new[] { "RowKey", "Value" },
                cancellationToken: ct))
            {
                if (row.TryGetValue("Value", out var val) && val is string s)
                    dict[row.RowKey] = s;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WorkerMetricsConfigReader: failed to read appconfig; using defaults");
            return defaults;
        }

        int ReadInt(string key, int fallback)
        {
            if (!dict.TryGetValue(key, out var v)) return fallback;
            if (int.TryParse(v, out var n)) return n;
            _logger.LogWarning("WorkerMetricsConfigReader: {Key} has non-integer value {Value}; using {Fallback}", key, v, fallback);
            return fallback;
        }

        return new WorkerMetricsConfig(
            RetentionMinuteHours: ReadInt(KeyRetentionMinuteHours, defaults.RetentionMinuteHours),
            RetentionHourDays:    ReadInt(KeyRetentionHourDays,    defaults.RetentionHourDays),
            RetentionDauDays:     ReadInt(KeyRetentionDauDays,     defaults.RetentionDauDays));
    }
}
