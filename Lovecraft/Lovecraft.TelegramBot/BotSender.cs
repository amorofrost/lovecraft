using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;

namespace Lovecraft.TelegramBot
{
    public class BotSender : IBotSender
    {
        private readonly Telegram.Bot.ITelegramBotClient _bot;

        public BotSender(Telegram.Bot.ITelegramBotClient bot)
        {
            _bot = bot;
        }

        public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
        {
            // Use the same extension helper as the rest of the project (SendMessage) which resolves in the Telegram.Bot namespace
            return _bot.SendMessage(chatId, text, cancellationToken: cancellationToken);
        }

        public Task SendPhotoAsync(long chatId, string fileId, string? caption = null, CancellationToken cancellationToken = default)
        {
            // send a photo using an existing file id (Telegram file id)
            // Use the same extension helper style as SendMessage to avoid direct dependency on Telegram.Bot input types
            return _bot.SendPhoto(chatId, fileId, caption: caption, cancellationToken: cancellationToken);
        }
    }
}
