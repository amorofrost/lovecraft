using System.Text;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;


namespace Lovecraft.TelegramBot
{
    public class BotSender : IBotSender
    {
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<BotSender> _log;

        public BotSender(ITelegramBotClient bot, ILogger<BotSender> logger)
        {
            _bot = bot;
            _log = logger;
        }

        public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
        {
            // Use the same extension helper as the rest of the project (SendMessage) which resolves in the Telegram.Bot namespace
            return _bot.SendMessage(chatId, text, cancellationToken: cancellationToken);
        }

        public Task SendPhotoAsync(long chatId, string fileId, string? caption = null, ReplyMarkup? replyMarkup = null, CancellationToken cancellationToken = default)
        {
            // send a photo using an existing file id (Telegram file id)
            // Use the same extension helper style as SendMessage to avoid direct dependency on Telegram.Bot input types
            return _bot.SendPhoto(chatId, fileId, caption: caption, cancellationToken: cancellationToken);
        }

        public async Task<string> SendProfileCardAsync(long chatId, Lovecraft.Common.DataContracts.User m, CancellationToken ct = default)
        {
            var captionSb = new StringBuilder()
                .AppendLine($"👤 {m.Name}");

            var caption = captionSb
                .ToString();

            var likeBtn = InlineKeyboardButton.WithCallbackData("👍 Like", $"like:{m.Id}");
            var kb = new InlineKeyboardMarkup(likeBtn);

            if (!string.IsNullOrWhiteSpace(m.TelegramAvatarFileId))
            {
                await this.SendPhotoAsync(chatId, m.TelegramAvatarFileId!, caption: caption, replyMarkup: kb, cancellationToken: ct);
                return m.TelegramAvatarFileId;
            }
            else if (!string.IsNullOrWhiteSpace(m.AvatarUri))
            {
                try
                {
                    var msg = await _bot.SendPhoto(chatId, m.AvatarUri!, caption: caption, replyMarkup: kb, cancellationToken: ct);
                    var fileId = msg.Photo!.FirstOrDefault()?.FileId;

                    if (fileId != null)
                    {
                        // Save the file_id for future use, if needed
                        m.TelegramAvatarFileId = fileId; // Update the member's photo with the file_id
                        return fileId;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"Failed to send photo for member {m.Id}");
                    await _bot.SendMessage(chatId, "(Не удалось загрузить фото)\r\n" + caption, replyMarkup: kb, cancellationToken: ct);
                }
            }
            else
            {
                await _bot.SendMessage(chatId, caption, replyMarkup: kb, cancellationToken: ct);
            }

            return string.Empty;
        }

        public Task AnswerCallbackQueryAsync(string callbackQueryId, string text, bool showAlert = false, CancellationToken cancellationToken = default)
        {
            return _bot.AnswerCallbackQuery(callbackQueryId, text, showAlert, cancellationToken: cancellationToken);
        }
    }
}
