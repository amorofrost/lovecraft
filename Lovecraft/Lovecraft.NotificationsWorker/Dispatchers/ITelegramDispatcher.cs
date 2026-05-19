using Lovecraft.NotificationsWorker.Models;

namespace Lovecraft.NotificationsWorker.Dispatchers;

public interface ITelegramDispatcher
{
    Task<DispatchResult> DispatchAsync(NotificationModel notification, CancellationToken ct);
}
