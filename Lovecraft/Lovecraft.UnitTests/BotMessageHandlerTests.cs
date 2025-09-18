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
        public Task SendPhotoAsync(long chatId, string fileId, string? caption = null, CancellationToken cancellationToken = default)
        {
            // Tests don't need to actually send photos; record as a message for assertions if needed
            LastChatId = chatId;
            LastText = caption ?? string.Empty;
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
            if (telegramUserId == 1)
            {
                return Task.FromResult<Lovecraft.Common.DataContracts.User?>(new Lovecraft.Common.DataContracts.User
                {
                    Id = System.Guid.NewGuid(),
                    Name = "test",
                    AvatarUri = "fileid",
                    CreatedAt = System.DateTime.UtcNow,
                    Version = System.Guid.NewGuid().ToString(),
                    TelegramUserId = telegramUserId,
                    TelegramUsername = "testuser",
                    TelegramAvatarFileId = "fileid"
                });
            }
            return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
        }

        public Task<Lovecraft.Common.DataContracts.User?> GetUserByTelegramUsernameAsync(string username)
        {
            return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
        }
    }

    class FakeNoAvatarApiClient : Lovecraft.Common.ILovecraftApiClient
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
            if (telegramUserId == 3)
            {
                return Task.FromResult<Lovecraft.Common.DataContracts.User?>(new Lovecraft.Common.DataContracts.User
                {
                    Id = System.Guid.NewGuid(),
                    Name = "NoAvatarUser",
                    AvatarUri = string.Empty,
                    CreatedAt = System.DateTime.UtcNow,
                    Version = System.Guid.NewGuid().ToString(),
                    TelegramUserId = telegramUserId,
                    TelegramUsername = "noavatar",
                    TelegramAvatarFileId = null
                });
            }
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
            try
            {
                var msg = new Message
                {
                    Chat = new Telegram.Bot.Types.Chat { Id = 12345 },
                    From = new Telegram.Bot.Types.User { Id = 1, Username = "testuser" },
                    Text = "/start ABC123"
                };

                await handler.HandleMessageAsync(msg, CancellationToken.None);

                Assert.AreEqual(12345, sender.LastChatId);
                Assert.IsTrue(sender.LastText.Contains("Health"));
            }
            finally
            {
                // restore previous value
                System.Environment.SetEnvironmentVariable("ACCESS_CODE", prev);
            }
        }

        [TestMethod]
        public async Task StartCommand_MissingAccessCode_SendsUnauthorized()
        {
            var sender = new FakeSender();
            var api = new FakeApiClient();
            var handler = new BotMessageHandler(sender, api);

            var prev = System.Environment.GetEnvironmentVariable("ACCESS_CODE");
            System.Environment.SetEnvironmentVariable("ACCESS_CODE", "ABC123");
            try
            {
                var msg = new Message
                {
                    Chat = new Telegram.Bot.Types.Chat { Id = 22222 },
                    From = new Telegram.Bot.Types.User { Id = 2, Username = "user2" },
                    Text = "/start"
                };

                await handler.HandleMessageAsync(msg, CancellationToken.None);

                Assert.AreEqual(22222, sender.LastChatId);
                Assert.IsTrue(sender.LastText.Contains("Вы не авторизованы"));
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("ACCESS_CODE", prev);
            }
        }

        [TestMethod]
        public async Task StartCommand_IncorrectAccessCode_SendsUnauthorized()
        {
            var sender = new FakeSender();
            var api = new FakeApiClient();
            var handler = new BotMessageHandler(sender, api);

            var prev = System.Environment.GetEnvironmentVariable("ACCESS_CODE");
            System.Environment.SetEnvironmentVariable("ACCESS_CODE", "ABC123");
            try
            {
                var msg = new Message
                {
                    Chat = new Telegram.Bot.Types.Chat { Id = 33333 },
                    From = new Telegram.Bot.Types.User { Id = 3, Username = "user3" },
                    Text = "/start WRONG"
                };

                await handler.HandleMessageAsync(msg, CancellationToken.None);

                Assert.AreEqual(33333, sender.LastChatId);
                Assert.IsTrue(sender.LastText.Contains("Вы не авторизованы"));
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("ACCESS_CODE", prev);
            }
        }

        [TestMethod]
        public async Task MeCommand_UserNotRegistered_PromptsToRegister()
        {
            var sender = new FakeSender();
            var api = new FakeApiClient(); // returns null for Telegram id != 1
            var handler = new BotMessageHandler(sender, api);

            var msg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 44444 },
                From = new Telegram.Bot.Types.User { Id = 2, Username = "noone" },
                Text = "/me"
            };

            await handler.HandleMessageAsync(msg, CancellationToken.None);

            Assert.AreEqual(44444, sender.LastChatId);
            Assert.IsTrue(sender.LastText.Contains("зарегистрируйтесь"));
        }

        [TestMethod]
        public async Task MeCommand_UserRegisteredWithPhoto_SendsPhotoAndName()
        {
            var sender = new FakeSender();
            var api = new FakeApiClient(); // returns a user for telegram id == 1
            var handler = new BotMessageHandler(sender, api);

            var msg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 55555 },
                From = new Telegram.Bot.Types.User { Id = 1, Username = "testuser" },
                Text = "/me"
            };

            await handler.HandleMessageAsync(msg, CancellationToken.None);

            Assert.AreEqual(55555, sender.LastChatId);
            // FakeSender stores caption of photo in LastText; user.Name is "test"
            Assert.IsTrue(sender.LastText.Contains("test"));
        }

        [TestMethod]
        public async Task MeCommand_UserRegisteredNoAvatar_SendsNameTextFallback()
        {
            var sender = new FakeSender();
            // Create a fake API client that returns a user without TelegramAvatarFileId for id == 3
            var api = new FakeNoAvatarApiClient();
            var handler = new BotMessageHandler(sender, api);

            var msg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 66666 },
                From = new Telegram.Bot.Types.User { Id = 3, Username = "noavatar" },
                Text = "/me"
            };

            await handler.HandleMessageAsync(msg, CancellationToken.None);

            Assert.AreEqual(66666, sender.LastChatId);
            // Should send a text fallback containing the user's name
            Assert.IsTrue(sender.LastText.Contains("NoAvatarUser") || sender.LastText.Contains("Имя"));
        }
    }
}

    [TestClass]
    public class BotMessageHandlerRegistrationTests
    {
        class RecordingSender : IBotSender
        {
            public long LastChatId;
            public readonly System.Collections.Generic.List<string> Messages = new();

            public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
            {
                LastChatId = chatId;
                Messages.Add(text);
                return Task.CompletedTask;
            }
            public Task SendPhotoAsync(long chatId, string fileId, string? caption = null, CancellationToken cancellationToken = default)
            {
                LastChatId = chatId;
                Messages.Add(caption ?? string.Empty);
                return Task.CompletedTask;
            }
        }

        class RecordingApiClient : Lovecraft.Common.ILovecraftApiClient
        {
            public Lovecraft.Common.DataContracts.CreateUserRequest? CreatedRequest;

            public Task<Lovecraft.Common.DataContracts.HealthInfo> GetHealthAsync()
            {
                return Task.FromResult(new Lovecraft.Common.DataContracts.HealthInfo { Ready = true, Version = "test", Uptime = System.TimeSpan.FromSeconds(1) });
            }

            public Task<Lovecraft.Common.DataContracts.User> CreateUserAsync(Lovecraft.Common.DataContracts.CreateUserRequest req)
            {
                CreatedRequest = req;
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
                // For registration tests, return null to indicate user not found
                return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
            }

            public Task<Lovecraft.Common.DataContracts.User?> GetUserByTelegramUsernameAsync(string username)
            {
                return Task.FromResult<Lovecraft.Common.DataContracts.User?>(null);
            }
        }

        [TestMethod]
        public async Task RegistrationHappyPath_CreatesUser()
        {
            var sender = new RecordingSender();
            var api = new RecordingApiClient();
            var handler = new BotMessageHandler(sender, api);

            var prev = System.Environment.GetEnvironmentVariable("ACCESS_CODE");
            System.Environment.SetEnvironmentVariable("ACCESS_CODE", "CODE123");

            // 1) send /start with code -> bot should ask for name since GetUserByTelegramUserIdAsync returns null
            var startMsg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 999 },
                From = new Telegram.Bot.Types.User { Id = 555, Username = "reguser" },
                Text = "/start CODE123",
            };
            await handler.HandleMessageAsync(startMsg, CancellationToken.None);

            Assert.IsTrue(sender.Messages.Exists(m => m.Contains("введите ваше имя")), "Bot did not ask for name after start");

            // 2) send name
            var nameMsg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 999 },
                From = new Telegram.Bot.Types.User { Id = 555, Username = "reguser" },
                Text = "Alice",
            };
            await handler.HandleMessageAsync(nameMsg, CancellationToken.None);

            Assert.IsTrue(sender.Messages.Exists(m => m.Contains("Теперь отправьте фотографию")), "Bot did not ask for photo after name");

            // 3) send photo message with a fake FileId
            var photoSize = new Telegram.Bot.Types.PhotoSize { FileId = "photo-file-id" };
            var photoMsg = new Message
            {
                Chat = new Telegram.Bot.Types.Chat { Id = 999 },
                From = new Telegram.Bot.Types.User { Id = 555, Username = "reguser" },
                Photo = new[] { photoSize },
            };

            await handler.HandleMessageAsync(photoMsg, CancellationToken.None);

            // Ensure API CreateUserAsync was called with expected values
            Assert.IsNotNull(api.CreatedRequest, "CreateUserAsync was not called");
            Assert.AreEqual("Alice", api.CreatedRequest!.Name);
            Assert.AreEqual("photo-file-id", api.CreatedRequest.AvatarUri);
            Assert.AreEqual(555, api.CreatedRequest.TelegramUserId);
            Assert.AreEqual("reguser", api.CreatedRequest.TelegramUsername);

            // Ensure a confirmation message was sent
            Assert.IsTrue(sender.Messages.Exists(m => m.Contains("Аккаунт создан")), "Bot did not send account created confirmation");

            System.Environment.SetEnvironmentVariable("ACCESS_CODE", prev);
        }
    }
