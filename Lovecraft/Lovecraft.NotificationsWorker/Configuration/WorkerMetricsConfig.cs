namespace Lovecraft.NotificationsWorker.Configuration;

/// <summary>
/// Minimal copy of the metrics retention settings from appconfig.
/// Mirrors <c>Lovecraft.Backend.Services.MetricsConfig</c> — keep in sync.
/// </summary>
public sealed record WorkerMetricsConfig(
    int RetentionMinuteHours,
    int RetentionHourDays,
    int RetentionDauDays)
{
    public static WorkerMetricsConfig Defaults => new(
        RetentionMinuteHours: 24,
        RetentionHourDays: 90,
        RetentionDauDays: 30);
}
