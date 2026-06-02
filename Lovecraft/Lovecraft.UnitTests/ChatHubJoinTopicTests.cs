using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Hubs;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class ChatHubJoinTopicTests
{
    private static ClaimsPrincipal User(string id) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }, "test"));

    private static ChatHub BuildHub(Mock<IForumTopicAccess> access, Mock<IGroupManager> groups)
    {
        var chat = new Mock<IChatService>();
        var presence = new Mock<IPresenceTracker>();
        var ctx = new Mock<HubCallerContext>();
        ctx.SetupGet(c => c.User).Returns(User("u1"));
        ctx.SetupGet(c => c.ConnectionId).Returns("conn-1");
        return new ChatHub(chat.Object, presence.Object, access.Object)
        {
            Context = ctx.Object,
            Groups = groups.Object,
        };
    }

    [Fact]
    public async Task JoinTopic_Throws_And_DoesNotJoin_WhenAccessDenied()
    {
        var access = new Mock<IForumTopicAccess>();
        access.Setup(a => a.CanViewTopicAsync(It.IsAny<ClaimsPrincipal>(), "event-attendees-e1")).ReturnsAsync(false);
        var groups = new Mock<IGroupManager>();
        var hub = BuildHub(access, groups);

        await Assert.ThrowsAsync<HubException>(() => hub.JoinTopic("event-attendees-e1"));
        groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JoinTopic_AddsToGroup_WhenAllowed()
    {
        var access = new Mock<IForumTopicAccess>();
        access.Setup(a => a.CanViewTopicAsync(It.IsAny<ClaimsPrincipal>(), "topic-ok")).ReturnsAsync(true);
        var groups = new Mock<IGroupManager>();
        groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        var hub = BuildHub(access, groups);

        await hub.JoinTopic("topic-ok");
        groups.Verify(g => g.AddToGroupAsync("conn-1", "topic-topic-ok", It.IsAny<CancellationToken>()), Times.Once);
    }
}
