using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.NotificationsWorker.Models;

namespace Lovecraft.NotificationsWorker.Renderers;

public interface IEmailDigestRenderer
{
    EmailRenderResultDto RenderSingle(NotificationModel notification, string unsubscribeToken);
    EmailRenderResultDto RenderDigest(DigestModel digest, string unsubscribeToken);
}
