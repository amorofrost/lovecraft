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
    }
}
