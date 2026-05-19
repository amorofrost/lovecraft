namespace Lovecraft.NotificationsWorker.Services;

/// <summary>
/// Scans the events table for events occurring ~24h in the future and writes
/// EventReminder notification rows (plus outbox rows for Telegram/Email channels)
/// for every attendee, with idempotency via a stable sourceEventId.
/// </summary>
public interface IEventReminderProcessor
{
    Task RunAsync(DateTime now, CancellationToken ct);
}
