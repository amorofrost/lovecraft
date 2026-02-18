using Xunit;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.MockData;
using System.Threading.Tasks;
using System.Linq;

namespace Lovecraft.UnitTests;

public class ServiceTests
{
    [Fact]
    public async Task UserService_GetUsers_ReturnsUsers()
    {
        // Arrange
        var service = new MockUserService();

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
        var service = new MockMatchingService();
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
        var service = new MockForumService();

        // Act
        var sections = await service.GetSectionsAsync();

        // Assert
        Assert.NotNull(sections);
        Assert.True(sections.Count > 0);
    }
}
