using Lovecraft.NotificationsWorker.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace Lovecraft.NotificationsWorker.Renderers;

public interface ITelegramMessageRenderer
{
    /// <summary>
    /// Renders a notification as Telegram HTML body + inline keyboard.
    /// Returns a sensible default if the notification type is unknown or the payload is malformed.
    /// </summary>
    (string Html, InlineKeyboardMarkup Keyboard) Render(NotificationModel notification);
}
