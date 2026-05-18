using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Workers;

public class DispatcherWorker : BackgroundService
{
    private static readonly string[] Channels = { "Telegram", "Email" };
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

    private readonly IOutboxProcessor _processor;
    private readonly ILogger<DispatcherWorker> _logger;

    public DispatcherWorker(IOutboxProcessor processor, ILogger<DispatcherWorker> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DispatcherWorker starting; tick interval {Interval}s", TickInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var channel in Channels)
            {
                try
                {
                    await _processor.ProcessChannelAsync(channel, stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DispatcherWorker channel {Channel} failed; will retry next tick", channel);
                }
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("DispatcherWorker stopped");
    }
}
