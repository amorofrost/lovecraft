using Azure;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Dispatchers;
using Lovecraft.NotificationsWorker.Entities;
using Lovecraft.NotificationsWorker.Models;
using Lovecraft.NotificationsWorker.Renderers;
using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class TelegramDispatcherTests
{
    private static NotificationModel SampleNotification(string type = "LikeReceived") =>
        new("n1", "user-abc", type, "actor-1", "{\"likeId\":\"l1\"}", DateTime.UtcNow);

    private static (TelegramDispatcher, Mock<ITelegramSendClient>) BuildDispatcher(
        string? telegramUserId,
        Func<Task>? sendBehavior = null,
        Mock<ITelegramRateLimiter>? rateLimiter = null)
    {
        var users = new Mock<TableClient>();
        if (telegramUserId is not null)
        {
            users.Setup(t => t.GetEntityAsync<UserContactEntity>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(new UserContactEntity { TelegramUserId = telegramUserId }, new Mock<Response>().Object));
        }
        else
        {
            users.Setup(t => t.GetEntityAsync<UserContactEntity>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "not found"));
        }

        var sendClient = new Mock<ITelegramSendClient>();
        if (sendBehavior is not null)
        {
            sendClient.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup>(), It.IsAny<CancellationToken>()))
                .Returns(sendBehavior);
        }
        else
        {
            sendClient.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        rateLimiter ??= new Mock<ITelegramRateLimiter>();
        rateLimiter.Setup(r => r.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var renderer = new TelegramMessageRenderer(NullLogger<TelegramMessageRenderer>.Instance);
        var dispatcher = new TelegramDispatcher(
            sendClient.Object, users.Object, renderer, rateLimiter.Object,
            NullLogger<TelegramDispatcher>.Instance);

        return (dispatcher, sendClient);
    }

    [Fact]
    public async Task Successful_send_returns_Delivered()
    {
        var (dispatcher, _) = BuildDispatcher(telegramUserId: "555111");

        var result = await dispatcher.DispatchAsync(SampleNotification(), CancellationToken.None);

        Assert.Equal(DispatchResult.Delivered, result);
    }

    [Fact]
    public async Task Missing_telegram_user_returns_PermanentError()
    {
        var (dispatcher, _) = BuildDispatcher(telegramUserId: null);

        var result = await dispatcher.DispatchAsync(SampleNotification(), CancellationToken.None);

        Assert.Equal(DispatchResult.PermanentError, result);
    }

    [Fact]
    public async Task Empty_telegram_id_returns_PermanentError()
    {
        var (dispatcher, _) = BuildDispatcher(telegramUserId: "");

        var result = await dispatcher.DispatchAsync(SampleNotification(), CancellationToken.None);

        Assert.Equal(DispatchResult.PermanentError, result);
    }

    [Fact]
    public async Task Bot_blocked_returns_PermanentError()
    {
        var (dispatcher, _) = BuildDispatcher(telegramUserId: "555111",
            sendBehavior: () => throw new Telegram.Bot.Exceptions.ApiRequestException("Forbidden: bot was blocked by the user", 403));

        var result = await dispatcher.DispatchAsync(SampleNotification(), CancellationToken.None);

        Assert.Equal(DispatchResult.PermanentError, result);
    }

    [Fact]
    public async Task Network_error_returns_RetryableError()
    {
        var (dispatcher, _) = BuildDispatcher(telegramUserId: "555111",
            sendBehavior: () => throw new HttpRequestException("network error"));

        var result = await dispatcher.DispatchAsync(SampleNotification(), CancellationToken.None);

        Assert.Equal(DispatchResult.RetryableError, result);
    }
}
