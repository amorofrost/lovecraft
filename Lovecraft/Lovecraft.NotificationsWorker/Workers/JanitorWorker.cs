using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Workers;

public class JanitorWorker : BackgroundService
{
    private static readonly TimeSpan DayInterval = TimeSpan.FromHours(24);
    private const int ScheduledHourUtc = 3;

    private readonly IOutboxJanitor _janitor;
    private readonly ILogger<JanitorWorker> _logger;

    public JanitorWorker(IOutboxJanitor janitor, ILogger<JanitorWorker> logger)
    {
        _janitor = janitor;
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
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JanitorWorker run failed");
            }

            try { await Task.Delay(DayInterval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
