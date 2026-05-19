using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Workers;

public class DigestWorker : BackgroundService
{
    private static readonly TimeSpan HourInterval = TimeSpan.FromHours(1);

    private readonly IDigestProcessor _processor;
    private readonly ILogger<DigestWorker> _logger;

    public DigestWorker(IDigestProcessor processor, ILogger<DigestWorker> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Sleep until top of next hour
        var now = DateTime.UtcNow;
        var nextHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);
        var initialDelay = nextHour - now;
        _logger.LogInformation("DigestWorker starting; first tick at {NextHour} (in {Delay})", nextHour, initialDelay);

        try { await Task.Delay(initialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _processor.ProcessAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DigestWorker tick failed; will retry next hour");
            }

            try { await Task.Delay(HourInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("DigestWorker stopped");
    }
}
