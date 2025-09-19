using Microsoft.VisualStudio.TestTools.UnitTesting;
using Lovecraft.TelegramBot;
using Lovecraft.UnitTests.Fakes;
using Telegram.Bot.Types;

namespace Lovecraft.UnitTests;
    
[TestClass]
public class BotMessageHandlerTests
{
    [TestMethod]
    public async Task StartCommand_SendsWeatherResponse_WithCorrectAccessCode()
    {
        var sender = new FakeSender();
        var api = new FakeApiClient();
        var accessCodeManager = new FakeAccessCodeManager();
        var fakeLogger = new FakeLogger<BotMessageHandler>();
        var handler = new BotMessageHandler(sender, api, accessCodeManager, fakeLogger);

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

    [TestMethod]
    public async Task StartCommand_MissingAccessCode_SendsUnauthorized()
    {
        var sender = new FakeSender();
        var api = new FakeApiClient();
        var accessCodeManager = new FakeAccessCodeManager();
        var fakeLogger = new FakeLogger<BotMessageHandler>();
        var handler = new BotMessageHandler(sender, api, accessCodeManager, fakeLogger);

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

    [TestMethod]
    public async Task StartCommand_IncorrectAccessCode_SendsUnauthorized()
    {
        var sender = new FakeSender();
        var api = new FakeApiClient();
        var accessCodeManager = new FakeAccessCodeManager();
        var fakeLogger = new FakeLogger<BotMessageHandler>();
        var handler = new BotMessageHandler(sender, api, accessCodeManager, fakeLogger);
        
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

    [TestMethod]
    public async Task MeCommand_UserNotRegistered_PromptsToRegister()
    {
        var sender = new FakeSender();
        var api = new FakeApiClient(); // returns null for Telegram id != 1
        var accessCodeManager = new FakeAccessCodeManager();
        var fakeLogger = new FakeLogger<BotMessageHandler>();
        var handler = new BotMessageHandler(sender, api, accessCodeManager, fakeLogger);

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
        var accessCodeManager = new FakeAccessCodeManager();
        var fakeLogger = new FakeLogger<BotMessageHandler>();
        var handler = new BotMessageHandler(sender, api, accessCodeManager, fakeLogger);

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
        var accessCodeManager = new FakeAccessCodeManager();
        var fakeLogger = new FakeLogger<BotMessageHandler>();
        var handler = new BotMessageHandler(sender, api, accessCodeManager, fakeLogger);

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

    [TestMethod]
    public async Task NextCommand_NoProfiles_SendsNotFoundMessage()
    {
        var sender = new FakeSender();
        var api = new FakeApiClient(); // returns null for GetNextProfileAsync
        var accessCodeManager = new FakeAccessCodeManager();
        var fakeLogger = new FakeLogger<BotMessageHandler>();
        var handler = new BotMessageHandler(sender, api, accessCodeManager, fakeLogger);

        var msg = new Message
        {
            Chat = new Telegram.Bot.Types.Chat { Id = 77777 },
            From = new Telegram.Bot.Types.User { Id = 10, Username = "whoever" },
            Text = "/next"
        };

        await handler.HandleMessageAsync(msg, CancellationToken.None);

        Assert.AreEqual(77777, sender.LastChatId);
        Assert.IsTrue(sender.LastText.Contains("Профили не найдены") || sender.LastText.Contains("не найд"));
    }

    [TestMethod]
    public async Task NextCommand_WithProfile_CallsSendProfileCard()
    {
        var recSender = new RecordingSender();
        var user = new Lovecraft.Common.DataContracts.User { Id = System.Guid.NewGuid(), Name = "NextUser", AvatarUri = "https://a" };
        var api = new FakeNextApiClient(user);
        var accessCodeManager = new FakeAccessCodeManager();
        var fakeLogger = new FakeLogger<BotMessageHandler>();
        var handler = new BotMessageHandler(recSender, api, accessCodeManager, fakeLogger);

        var msg = new Message
        {
            Chat = new Telegram.Bot.Types.Chat { Id = 88888 },
            From = new Telegram.Bot.Types.User { Id = 11, Username = "someone" },
            Text = "/next"
        };

        await handler.HandleMessageAsync(msg, CancellationToken.None);

        Assert.AreEqual(88888, recSender.LastChatId);
        Assert.IsTrue(recSender.Messages.Exists(m => m.Contains("Profile card for NextUser")));
    }
}

[TestClass]
public class BotMessageHandlerRegistrationTests
{
    [TestMethod]
    public async Task RegistrationHappyPath_CreatesUser()
    {
        var sender = new RecordingSender();
        var api = new RecordingApiClient();
        var accessCodeManager = new FakeAccessCodeManager();
        var fakeLogger = new FakeLogger<BotMessageHandler>();
        var handler = new BotMessageHandler(sender, api, accessCodeManager, fakeLogger);

        // 1) send /start with code -> bot should ask for name since GetUserByTelegramUserIdAsync returns null
        var startMsg = new Message
        {
            Chat = new Telegram.Bot.Types.Chat { Id = 999 },
            From = new Telegram.Bot.Types.User { Id = 555, Username = "reguser" },
            Text = "/start ABC123",
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

        await handler.HandlePhotoAsync(photoMsg, CancellationToken.None);

        // Ensure API CreateUserAsync was called with expected values
        Assert.IsNotNull(api.CreatedRequest, "CreateUserAsync was not called");
        Assert.AreEqual("Alice", api.CreatedRequest!.Name);
        Assert.AreEqual("photo-file-id", api.CreatedRequest.AvatarUri);
        Assert.AreEqual(555, api.CreatedRequest.TelegramUserId);
        Assert.AreEqual("reguser", api.CreatedRequest.TelegramUsername);

        // Ensure a confirmation message was sent
        Assert.IsTrue(sender.Messages.Exists(m => m.Contains("Аккаунт создан")), "Bot did not send account created confirmation");
    }
}
