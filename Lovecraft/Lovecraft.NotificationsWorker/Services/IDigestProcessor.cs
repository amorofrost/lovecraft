namespace Lovecraft.NotificationsWorker.Services;

public interface IDigestProcessor
{
    /// <summary>
    /// Aggregates and dispatches Hourly + Daily outbox rows.
    /// `now` is taken as a parameter so tests can pin the wall clock.
    /// </summary>
    Task ProcessAsync(DateTime now, CancellationToken ct);
}
