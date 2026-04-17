using Xunit;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.MockData;
using Lovecraft.Common.Enums;
using System.Threading.Tasks;
using System.Linq;

namespace Lovecraft.UnitTests;

public class ServiceTests
{
    [Fact]
    public async Task UserService_GetUsers_ReturnsUsers()
    {
        // Arrange
        var service = new MockUserService(new MockAppConfigService());

        // Act
        var users = await service.GetUsersAsync();

        // Assert
        Assert.NotNull(users);
        Assert.True(users.Count > 0);
    }

    [Fact]
    public async Task EventService_GetEvents_ReturnsEvents()
    {
        // Arrange
        var service = new MockEventService();

        // Act
        var events = await service.GetEventsAsync();

        // Assert
        Assert.NotNull(events);
        Assert.True(events.Count > 0);
    }

    [Fact]
    public async Task MatchingService_CreateLike_CreatesLike()
    {
        // Arrange
        var service = new MockMatchingService(new MockChatService(), new MockUserService(new MockAppConfigService()));
        var fromUserId = "user1";
        var toUserId = "user2";

        // Act
        var result = await service.CreateLikeAsync(fromUserId, toUserId);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Like);
        Assert.Equal(fromUserId, result.Like.FromUserId);
        Assert.Equal(toUserId, result.Like.ToUserId);
    }

    [Fact]
    public async Task StoreService_GetStoreItems_ReturnsItems()
    {
        // Arrange
        var service = new MockStoreService();

        // Act
        var items = await service.GetStoreItemsAsync();

        // Assert
        Assert.NotNull(items);
        Assert.True(items.Count > 0);
    }

    [Fact]
    public async Task BlogService_GetBlogPosts_ReturnsPosts()
    {
        // Arrange
        var service = new MockBlogService();

        // Act
        var posts = await service.GetBlogPostsAsync();

        // Assert
        Assert.NotNull(posts);
        Assert.True(posts.Count > 0);
    }

    [Fact]
    public async Task ForumService_GetSections_ReturnsSections()
    {
        // Arrange
        var service = new MockForumService(new MockUserService(new MockAppConfigService()));

        // Act
        var sections = await service.GetSectionsAsync();

        // Assert
        Assert.NotNull(sections);
        Assert.True(sections.Count > 0);
    }

    [Fact]
    public async Task MockUserService_NoActivity_ReturnsNovice()
    {
        var svc = new MockUserService(new MockAppConfigService());
        var user = await svc.GetUserByIdAsync("1");
        Assert.NotNull(user);
        Assert.Equal(UserRank.Novice, user!.Rank);
        Assert.Equal(StaffRole.None, user.StaffRole);
    }

    [Fact]
    public async Task MockUserService_CrewReplyCount_ReturnsAloeCrew()
    {
        MockDataStore.UserActivity["1"] = new MockUserActivity { ReplyCount = 100 };
        try
        {
            var svc = new MockUserService(new MockAppConfigService());
            var user = await svc.GetUserByIdAsync("1");
            Assert.Equal(UserRank.AloeCrew, user!.Rank);
        }
        finally
        {
            MockDataStore.UserActivity.Clear();
        }
    }

    [Fact]
    public async Task MockUserService_IncrementCounter_PromotesTier()
    {
        MockDataStore.UserActivity.Clear();
        var svc = new MockUserService(new MockAppConfigService());
        for (int i = 0; i < 5; i++)
            await svc.IncrementCounterAsync("1", UserCounter.ReplyCount);
        var user = await svc.GetUserByIdAsync("1");
        Assert.Equal(UserRank.ActiveMember, user!.Rank);
        MockDataStore.UserActivity.Clear();
    }
}
