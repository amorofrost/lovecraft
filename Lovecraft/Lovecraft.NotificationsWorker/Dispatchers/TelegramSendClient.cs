using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Lovecraft.NotificationsWorker.Dispatchers;

/// <summary>
/// Production implementation: delegates to the Telegram.Bot SDK extension method SendMessage.
/// Registered in DI when TELEGRAM_BOT_TOKEN is present (Task 6).
/// </summary>
public class TelegramSendClient : ITelegramSendClient
{
    private readonly ITelegramBotClient _bot;

    public TelegramSendClient(ITelegramBotClient bot)
    {
        _bot = bot;
    }

    public Task SendAsync(string chatId, string html, InlineKeyboardMarkup keyboard, CancellationToken ct) =>
        _bot.SendMessage(
            chatId: chatId,
            text: html,
            parseMode: ParseMode.Html,
            replyMarkup: keyboard,
            cancellationToken: ct);
}
