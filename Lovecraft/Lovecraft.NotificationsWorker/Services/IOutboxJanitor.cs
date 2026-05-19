namespace Lovecraft.NotificationsWorker.Services;

public interface IOutboxJanitor
{
    Task RunAsync(DateTime now, CancellationToken ct);
}
