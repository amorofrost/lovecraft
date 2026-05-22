using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Services.Metrics;

public sealed class MetricsConfigPoller : BackgroundService
{
    private readonly IMetricsCollector _collector;
    private readonly IAppConfigService _appConfig;
    private readonly ILogger<MetricsConfigPoller> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    public MetricsConfigPoller(IMetricsCollector collector, IAppConfigService appConfig, ILogger<MetricsConfigPoller> logger)
    {
        _collector = collector;
        _appConfig = appConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ApplyAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(PollInterval, stoppingToken);
            await ApplyAsync(stoppingToken);
        }
    }

    private async Task ApplyAsync(CancellationToken ct)
    {
        try
        {
            var cfg = await _appConfig.GetMetricsConfigAsync(ct);
            _collector.UpdateFlags(new MetricsEnabledFlags(cfg.RequestTiming, cfg.BiEvents, cfg.ContainerStats, cfg.FrontendPerf));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MetricsConfigPoller refresh failed; keeping previous flags");
        }
    }
}
