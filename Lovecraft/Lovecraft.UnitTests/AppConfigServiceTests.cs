using Lovecraft.Backend.Services;
using Xunit;

namespace Lovecraft.UnitTests;

public class MockAppConfigServiceTests
{
    [Fact]
    public async Task GetConfigAsync_ReturnsDefaultRankThresholds()
    {
        var service = new MockAppConfigService();
        var config = await service.GetConfigAsync();

        Assert.Equal(5, config.Ranks.ActiveReplies);
        Assert.Equal(15, config.Ranks.FriendLikes);
        Assert.Equal(100, config.Ranks.CrewReplies);
        Assert.Equal(10, config.Ranks.CrewMatches);
    }

    [Fact]
    public async Task GetConfigAsync_ReturnsDefaultPermissions()
    {
        var service = new MockAppConfigService();
        var config = await service.GetConfigAsync();

        Assert.Equal("activeMember", config.Permissions.CreateTopic);
        Assert.Equal("moderator", config.Permissions.DeleteAnyReply);
        Assert.Equal("admin", config.Permissions.AssignRole);
    }
}
