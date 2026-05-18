using Lovecraft.NotificationsWorker.Models;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Dispatchers;

/// <summary>
/// Phase C stub. Logs the dispatch and returns Delivered.
/// Replace with real implementation in Phase D (Telegram.Bot SendMessage + inline keyboard).
/// </summary>
public class StubTelegramDispatcher : ITelegramDispatcher
{
    private readonly ILogger<StubTelegramDispatcher> _logger;

    public StubTelegramDispatcher(ILogger<StubTelegramDispatcher> logger)
    {
        _logger = logger;
    }

    public Task<DispatchResult> DispatchAsync(NotificationModel notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[STUB Telegram] would dispatch notification {NotificationId} ({Type}) to user {UserId}",
            notification.NotificationId, notification.Type, notification.UserId);
        return Task.FromResult(DispatchResult.Delivered);
    }
}
