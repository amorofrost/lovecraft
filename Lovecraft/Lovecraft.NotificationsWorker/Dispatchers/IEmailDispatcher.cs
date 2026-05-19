using Lovecraft.NotificationsWorker.Models;

namespace Lovecraft.NotificationsWorker.Dispatchers;

public interface IEmailDispatcher
{
    Task<DispatchResult> DispatchAsync(NotificationModel notification, CancellationToken ct);
}
