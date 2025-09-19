using Telegram.Bot.Types.ReplyMarkups;
using Lovecraft.TelegramBot;

namespace Lovecraft.UnitTests.Fakes;

internal class RecordingSender : IBotSender
{
    public long LastChatId;
    public readonly System.Collections.Generic.List<string> Messages = new();

    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        LastChatId = chatId;
        Messages.Add(text);
        return Task.CompletedTask;
    }

    public Task SendPhotoAsync(long chatId, string fileId, string? caption = null, ReplyMarkup? replyMarkup = null, CancellationToken cancellationToken = default)
    {
        LastChatId = chatId;
        Messages.Add(caption ?? string.Empty);
        return Task.CompletedTask;
    }

    public Task<string> SendProfileCardAsync(long chatId, Lovecraft.Common.DataContracts.User user, CancellationToken cancellationToken = default)
    {
        LastChatId = chatId;
        Messages.Add($"Profile card for {user.Name}");
        return Task.FromResult("recorded_file_id");
    }
}