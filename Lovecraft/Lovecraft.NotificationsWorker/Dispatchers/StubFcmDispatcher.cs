using Lovecraft.NotificationsWorker.Models;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Dispatchers;

/// <summary>
/// Stand-in until Firebase credentials exist (Phase 2C). Logs the dispatch and returns Delivered.
/// Replace with a real FcmDispatcher (Firebase Admin / FCM HTTP v1, data messages, dead-token pruning)
/// once FCM_SERVICE_ACCOUNT_JSON is configured.
/// </summary>
public class StubFcmDispatcher : IFcmDispatcher
{
    private readonly ILogger<StubFcmDispatcher> _logger;
    public StubFcmDispatcher(ILogger<StubFcmDispatcher> logger) => _logger = logger;

    public Task<DispatchResult> DispatchAsync(NotificationModel notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[STUB Fcm] would dispatch notification {NotificationId} ({Type}) to user {UserId}",
            notification.NotificationId, notification.Type, notification.UserId);
        return Task.FromResult(DispatchResult.Delivered);
    }
}
