using Lovecraft.NotificationsWorker.Dispatchers;
using Lovecraft.NotificationsWorker.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class StubDispatcherTests
{
    private static NotificationModel SampleNotification() =>
        new("n1", "u1", "LikeReceived", "u2", "{}", DateTime.UtcNow);

    [Fact]
    public async Task StubTelegramDispatcher_returns_Delivered()
    {
        var dispatcher = new StubTelegramDispatcher(NullLogger<StubTelegramDispatcher>.Instance);
        var result = await dispatcher.DispatchAsync(SampleNotification(), CancellationToken.None);
        Assert.Equal(DispatchResult.Delivered, result);
    }

    [Fact]
    public async Task StubEmailDispatcher_returns_Delivered()
    {
        var dispatcher = new StubEmailDispatcher(NullLogger<StubEmailDispatcher>.Instance);
        var result = await dispatcher.DispatchAsync(SampleNotification(), CancellationToken.None);
        Assert.Equal(DispatchResult.Delivered, result);
    }
}
