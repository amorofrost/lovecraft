namespace Lovecraft.Backend.Services.Metrics;

public sealed record MetricsEnabledFlags(
    bool RequestTiming = true,
    bool BiEvents = true,
    bool ContainerStats = true,
    bool FrontendPerf = true)
{
    public static MetricsEnabledFlags AllEnabled => new(true, true, true, true);
    public static MetricsEnabledFlags AllDisabled => new(false, false, false, false);

    public bool IsEnabled(string category) => category switch
    {
        "request_timing" => RequestTiming,
        "bi_events" => BiEvents,
        "container_stats" => ContainerStats,
        "frontend_perf" => FrontendPerf,
        _ => false,
    };
}
