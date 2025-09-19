using Lovecraft.TelegramBot;
using Telegram.Bot.Types.ReplyMarkups;

namespace Lovecraft.UnitTests.Fakes;

class FakeSender : IBotSender
{
    public long LastChatId;
    public string LastText = string.Empty;

    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        LastChatId = chatId;
        LastText = text;
        return Task.CompletedTask;
    }
    public Task SendPhotoAsync(long chatId, string fileId, string? caption = null, ReplyMarkup? replyMarkup = null, CancellationToken cancellationToken = default)
    {
        // Tests don't need to actually send photos; record as a message for assertions if needed
        LastChatId = chatId;
        LastText = caption ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string> SendProfileCardAsync(long chatId, Lovecraft.Common.DataContracts.User user, CancellationToken cancellationToken = default)
    {
        LastChatId = chatId;
        LastText = $"Profile card for {user.Name}";
        return Task.FromResult("fake_file_id");
    }
}