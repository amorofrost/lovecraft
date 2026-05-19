using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Workers;

public class EventReminderWorker : BackgroundService
{
    private readonly IEventReminderProcessor _processor;
    private readonly ILogger<EventReminderWorker> _logger;
    private readonly TimeSpan _interval;

    public EventReminderWorker(IEventReminderProcessor processor, ILogger<EventReminderWorker> logger)
    {
        _processor = processor;
        _logger = logger;
        var raw = Environment.GetEnvironmentVariable("NOTIFICATIONS_WORKER_REMINDER_SCAN_INTERVAL_MINUTES");
        var minutes = int.TryParse(raw, out var m) && m > 0 ? m : 5;
        _interval = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventReminderWorker starting; tick interval {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _processor.RunAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EventReminderWorker tick failed");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("EventReminderWorker stopped");
    }
}
