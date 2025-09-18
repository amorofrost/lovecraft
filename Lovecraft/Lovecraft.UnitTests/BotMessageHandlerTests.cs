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
        public Task<Lovecraft.Common.DataContracts.HealthInfo> GetHealthAsync()
        {
            return Task.FromResult(new Lovecraft.Common.DataContracts.HealthInfo { Ready = true, Version = "test", Uptime = System.TimeSpan.FromSeconds(1) });
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
        public async Task StartCommand_SendsWeatherResponse_WithCorrectAccessCode()
        {
            var sender = new FakeSender();
            var api = new FakeApiClient();
            var handler = new BotMessageHandler(sender, api);

            // Set expected access code in environment for the handler to read
            var prev = System.Environment.GetEnvironmentVariable("ACCESS_CODE");
            System.Environment.SetEnvironmentVariable("ACCESS_CODE", "ABC123");

            var msg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 12345 },
                From = new Telegram.Bot.Types.User { Id = 1, Username = "testuser" },
                Text = "/start ABC123"
            };

            await handler.HandleMessageAsync(msg, CancellationToken.None);

            Assert.AreEqual(12345, sender.LastChatId);
            Assert.IsTrue(sender.LastText.Contains("Health"));

            // restore previous value
            System.Environment.SetEnvironmentVariable("ACCESS_CODE", prev);
        }

        [TestMethod]
        public async Task StartCommand_MissingAccessCode_SendsUnauthorized()
        {
            var sender = new FakeSender();
            var api = new FakeApiClient();
            var handler = new BotMessageHandler(sender, api);

            var prev = System.Environment.GetEnvironmentVariable("ACCESS_CODE");
            System.Environment.SetEnvironmentVariable("ACCESS_CODE", "ABC123");

            var msg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 22222 },
                From = new Telegram.Bot.Types.User { Id = 2, Username = "user2" },
                Text = "/start"
            };

            await handler.HandleMessageAsync(msg, CancellationToken.None);

            Assert.AreEqual(22222, sender.LastChatId);
            Assert.IsTrue(sender.LastText.Contains("Вы не авторизованы"));

            System.Environment.SetEnvironmentVariable("ACCESS_CODE", prev);
        }

        [TestMethod]
        public async Task StartCommand_IncorrectAccessCode_SendsUnauthorized()
        {
            var sender = new FakeSender();
            var api = new FakeApiClient();
            var handler = new BotMessageHandler(sender, api);

            var prev = System.Environment.GetEnvironmentVariable("ACCESS_CODE");
            System.Environment.SetEnvironmentVariable("ACCESS_CODE", "ABC123");

            var msg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 33333 },
                From = new Telegram.Bot.Types.User { Id = 3, Username = "user3" },
                Text = "/start WRONG"
            };

            await handler.HandleMessageAsync(msg, CancellationToken.None);

            Assert.AreEqual(33333, sender.LastChatId);
            Assert.IsTrue(sender.LastText.Contains("Вы не авторизованы"));

            System.Environment.SetEnvironmentVariable("ACCESS_CODE", prev);
        }
    }
}
