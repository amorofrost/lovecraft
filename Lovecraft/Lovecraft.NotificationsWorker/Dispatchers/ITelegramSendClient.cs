using Telegram.Bot.Types.ReplyMarkups;

namespace Lovecraft.NotificationsWorker.Dispatchers;

/// <summary>
/// Thin wrapper around ITelegramBotClient.SendMessage (an extension method) to enable unit-test mocking.
/// The real implementation delegates to Telegram.Bot SDK; tests inject a mock directly.
/// </summary>
public interface ITelegramSendClient
{
    Task SendAsync(string chatId, string html, InlineKeyboardMarkup keyboard, CancellationToken ct);
}
