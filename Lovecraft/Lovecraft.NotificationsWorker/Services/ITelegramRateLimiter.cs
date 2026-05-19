namespace Lovecraft.NotificationsWorker.Services;

public interface ITelegramRateLimiter
{
    /// <summary>
    /// Acquires a slot for sending to this chat. Blocks until rate limit allows.
    /// Per-chat: minimum 1s between sends. Per-bot: global concurrency cap (25).
    /// </summary>
    Task AcquireAsync(string chatId, CancellationToken ct);
}
