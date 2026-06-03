using Lovecraft.NotificationsWorker.Models;

namespace Lovecraft.NotificationsWorker.Dispatchers;

public interface IFcmDispatcher
{
    Task<DispatchResult> DispatchAsync(NotificationModel notification, CancellationToken ct);
}
