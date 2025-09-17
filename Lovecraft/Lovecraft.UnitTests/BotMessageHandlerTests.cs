using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lovecraft.TelegramBot;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using System.Threading;

namespace Lovecraft.UnitTests
{
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
    }

    class FakeApiClient : Lovecraft.Common.ILovecraftApiClient
    {
        public Task<string> GetWeatherAsync()
        {
            return Task.FromResult("[{\"Summary\":\"Test\", \"TemperatureC\":25}] ");
        }

        public Task<Lovecraft.Common.DataContracts.User> CreateUserAsync(Lovecraft.Common.DataContracts.CreateUserRequest req)
        {
            var u = new Lovecraft.Common.DataContracts.User
            {
                Id = System.Guid.NewGuid(),
                Name = req.Name,
                AvatarUri = req.AvatarUri,
                TelegramUserId = req.TelegramUserId,
                TelegramUsername = req.TelegramUsername,
                TelegramAvatarFileId = req.TelegramAvatarFileId,
                CreatedAt = System.DateTime.UtcNow,
                Version = System.Guid.NewGuid().ToString()
            };
            return Task.FromResult(u);
        }

        public Task<Lovecraft.Common.DataContracts.User?> GetUserByIdAsync(System.Guid id)
        {
            return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
        }

        public Task<Lovecraft.Common.DataContracts.User?> GetUserByTelegramUserIdAsync(long telegramUserId)
        {
            return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
        }

        public Task<Lovecraft.Common.DataContracts.User?> GetUserByTelegramUsernameAsync(string username)
        {
            return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
        }
    }

    [TestClass]
    public class BotMessageHandlerTests
    {
        [TestMethod]
        public async Task StartCommand_SendsWeatherResponse()
        {
            var sender = new FakeSender();
            var api = new FakeApiClient();
            var handler = new BotMessageHandler(sender, api);

            var msg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 12345 },
                From = new Telegram.Bot.Types.User { Id = 1, Username = "testuser" },
                Text = "/start"
            };

            await handler.HandleMessageAsync(msg, CancellationToken.None);

            Assert.AreEqual(12345, sender.LastChatId);
            Assert.IsTrue(sender.LastText.Contains("WeatherForecast"));
        }
    }
}
