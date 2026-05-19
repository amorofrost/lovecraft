namespace Lovecraft.NotificationsWorker.Models;

public record DigestModel(string UserId, IReadOnlyList<NotificationModel> Members);
