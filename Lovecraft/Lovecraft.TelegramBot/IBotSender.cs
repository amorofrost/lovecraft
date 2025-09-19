using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types.ReplyMarkups;

namespace Lovecraft.TelegramBot
{
    public interface IBotSender
    {
        Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
        Task SendPhotoAsync(long chatId, string fileId, string? caption = null, ReplyMarkup? replyMarkup = null, CancellationToken cancellationToken = default);
        Task<string> SendProfileCardAsync(long chatId, Common.DataContracts.User user, CancellationToken cancellationToken = default);
        Task AnswerCallbackQueryAsync(string callbackQueryId, string text, bool showAlert = false, CancellationToken cancellationToken = default);
    }
}
