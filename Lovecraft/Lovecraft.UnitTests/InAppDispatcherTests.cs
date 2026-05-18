using Lovecraft.Backend.Hubs;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class InAppDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_sends_NotificationReceived_to_user_group()
    {
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.User("u1")).Returns(clientProxy.Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var dispatcher = new InAppDispatcher(hub.Object);
        var dto = new NotificationDto { Id = "n1", UserId = "u1", Type = NotificationType.LikeReceived };

        await dispatcher.DispatchAsync("u1", dto);

        clientProxy.Verify(c => c.SendCoreAsync(
            "NotificationReceived",
            It.Is<object[]>(args => args.Length == 1 && args[0] == dto),
            default), Times.Once);
    }
}
