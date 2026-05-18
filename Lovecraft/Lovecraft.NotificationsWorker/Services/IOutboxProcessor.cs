namespace Lovecraft.NotificationsWorker.Services;

/// <summary>
/// Drains pending outbox rows for one channel, dispatches via the channel's dispatcher,
/// and moves rows to DONE/DEAD/PENDING-with-backoff based on dispatch result.
/// </summary>
public interface IOutboxProcessor
{
    Task ProcessChannelAsync(string channel, CancellationToken ct);
}
