using Lovecraft.NotificationsWorker.Models;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Dispatchers;

/// <summary>
/// Phase C stub. Logs the dispatch and returns Delivered.
/// Replace with real SendGrid digest renderer in Phase F.
/// </summary>
public class StubEmailDispatcher : IEmailDispatcher
{
    private readonly ILogger<StubEmailDispatcher> _logger;

    public StubEmailDispatcher(ILogger<StubEmailDispatcher> logger)
    {
        _logger = logger;
    }

    public Task<DispatchResult> DispatchAsync(NotificationModel notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[STUB Email] would dispatch notification {NotificationId} ({Type}) to user {UserId}",
            notification.NotificationId, notification.Type, notification.UserId);
        return Task.FromResult(DispatchResult.Delivered);
    }
}
