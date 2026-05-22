using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Services.Metrics;

public sealed class MetricsFlushWorker : BackgroundService
{
    private readonly IMetricsCollector _collector;
    private readonly ILogger<MetricsFlushWorker> _logger;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);

    public MetricsFlushWorker(IMetricsCollector collector, ILogger<MetricsFlushWorker> logger)
    {
        _collector = collector;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _collector.FlushAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metrics flush failed");
            }
            await Task.Delay(FlushInterval, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try { await _collector.FlushAsync(cancellationToken); } catch { }
    }
}
