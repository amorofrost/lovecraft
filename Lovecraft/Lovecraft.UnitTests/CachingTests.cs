using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Caching;
using Lovecraft.Common.DTOs.Blog;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Store;
using Lovecraft.Common.Models;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace Lovecraft.UnitTests;

/// <summary>
/// Unit tests for the caching decorator services.
///
/// Each test uses a real MemoryCache (so TTL and eviction behave correctly)
/// and a Moq mock for the inner service.  The key behaviours tested are:
///   - Cache miss: inner service is called and result is stored.
///   - Cache hit: inner service is NOT called on subsequent reads.
///   - Null not cached: a null result from the inner service is not stored,
///     so the next call reaches the inner service again.
///   - Invalidation: write operations remove the relevant cache entries so
///     the next read fetches fresh data.
/// </summary>
public class CachingTests
{
    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    // -------------------------------------------------------------------------
    // CachingEventService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Events_GetAll_CacheMiss_CallsInner()
    {
        var inner = new Mock<IEventService>();
        inner.Setup(s => s.GetEventsAsync()).ReturnsAsync(new List<EventDto> { new() });
        var svc = new CachingEventService(inner.Object, NewCache());

        await svc.GetEventsAsync();

        inner.Verify(s => s.GetEventsAsync(), Times.Once);
    }

    [Fact]
    public async Task Events_GetAll_CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<IEventService>();
        inner.Setup(s => s.GetEventsAsync()).ReturnsAsync(new List<EventDto> { new() });
        var svc = new CachingEventService(inner.Object, NewCache());

        await svc.GetEventsAsync(); // populates cache
        await svc.GetEventsAsync(); // should hit cache

        inner.Verify(s => s.GetEventsAsync(), Times.Once);
    }

    [Fact]
    public async Task Events_GetById_CacheMiss_CallsInner()
    {
        var inner = new Mock<IEventService>();
        inner.Setup(s => s.GetEventByIdAsync("e1")).ReturnsAsync(new EventDto { Id = "e1" });
        var svc = new CachingEventService(inner.Object, NewCache());

        await svc.GetEventByIdAsync("e1");

        inner.Verify(s => s.GetEventByIdAsync("e1"), Times.Once);
    }

    [Fact]
    public async Task Events_GetById_CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<IEventService>();
        inner.Setup(s => s.GetEventByIdAsync("e1")).ReturnsAsync(new EventDto { Id = "e1" });
        var svc = new CachingEventService(inner.Object, NewCache());

        await svc.GetEventByIdAsync("e1"); // populates cache
        await svc.GetEventByIdAsync("e1"); // cache hit

        inner.Verify(s => s.GetEventByIdAsync("e1"), Times.Once);
    }

    [Fact]
    public async Task Events_GetById_NullNotCached()
    {
        var inner = new Mock<IEventService>();
        inner.Setup(s => s.GetEventByIdAsync("missing")).ReturnsAsync((EventDto?)null);
        var svc = new CachingEventService(inner.Object, NewCache());

        await svc.GetEventByIdAsync("missing");
        await svc.GetEventByIdAsync("missing");

        inner.Verify(s => s.GetEventByIdAsync("missing"), Times.Exactly(2));
    }

    [Fact]
    public async Task Events_Register_InvalidatesCache()
    {
        var inner = new Mock<IEventService>();
        inner.Setup(s => s.GetEventsAsync()).ReturnsAsync(new List<EventDto> { new() });
        inner.Setup(s => s.RegisterForEventAsync("u1", "e1")).ReturnsAsync(true);
        var svc = new CachingEventService(inner.Object, NewCache());

        await svc.GetEventsAsync();           // populates cache
        await svc.RegisterForEventAsync("u1", "e1"); // invalidates
        await svc.GetEventsAsync();           // must re-fetch

        inner.Verify(s => s.GetEventsAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task Events_Unregister_AlwaysInvalidatesCache()
    {
        var inner = new Mock<IEventService>();
        inner.Setup(s => s.GetEventsAsync()).ReturnsAsync(new List<EventDto> { new() });
        // Returns false (e.g. already unregistered) but cache must still be cleared
        inner.Setup(s => s.UnregisterFromEventAsync("u1", "e1")).ReturnsAsync(false);
        var svc = new CachingEventService(inner.Object, NewCache());

        await svc.GetEventsAsync();                    // populates cache
        await svc.UnregisterFromEventAsync("u1", "e1"); // invalidates even on false
        await svc.GetEventsAsync();                    // must re-fetch

        inner.Verify(s => s.GetEventsAsync(), Times.Exactly(2));
    }

    // -------------------------------------------------------------------------
    // CachingBlogService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Blog_GetAll_CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<IBlogService>();
        inner.Setup(s => s.GetBlogPostsAsync()).ReturnsAsync(new List<BlogPostDto> { new() });
        var svc = new CachingBlogService(inner.Object, NewCache());

        await svc.GetBlogPostsAsync();
        await svc.GetBlogPostsAsync();

        inner.Verify(s => s.GetBlogPostsAsync(), Times.Once);
    }

    [Fact]
    public async Task Blog_GetById_CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<IBlogService>();
        inner.Setup(s => s.GetBlogPostByIdAsync("p1")).ReturnsAsync(new BlogPostDto { Id = "p1" });
        var svc = new CachingBlogService(inner.Object, NewCache());

        await svc.GetBlogPostByIdAsync("p1");
        await svc.GetBlogPostByIdAsync("p1");

        inner.Verify(s => s.GetBlogPostByIdAsync("p1"), Times.Once);
    }

    [Fact]
    public async Task Blog_GetById_NullNotCached()
    {
        var inner = new Mock<IBlogService>();
        inner.Setup(s => s.GetBlogPostByIdAsync("missing")).ReturnsAsync((BlogPostDto?)null);
        var svc = new CachingBlogService(inner.Object, NewCache());

        await svc.GetBlogPostByIdAsync("missing");
        await svc.GetBlogPostByIdAsync("missing");

        inner.Verify(s => s.GetBlogPostByIdAsync("missing"), Times.Exactly(2));
    }

    // -------------------------------------------------------------------------
    // CachingStoreService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Store_GetAll_CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<IStoreService>();
        inner.Setup(s => s.GetStoreItemsAsync()).ReturnsAsync(new List<StoreItemDto> { new() });
        var svc = new CachingStoreService(inner.Object, NewCache());

        await svc.GetStoreItemsAsync();
        await svc.GetStoreItemsAsync();

        inner.Verify(s => s.GetStoreItemsAsync(), Times.Once);
    }

    [Fact]
    public async Task Store_GetById_CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<IStoreService>();
        inner.Setup(s => s.GetStoreItemByIdAsync("s1")).ReturnsAsync(new StoreItemDto { Id = "s1" });
        var svc = new CachingStoreService(inner.Object, NewCache());

        await svc.GetStoreItemByIdAsync("s1");
        await svc.GetStoreItemByIdAsync("s1");

        inner.Verify(s => s.GetStoreItemByIdAsync("s1"), Times.Once);
    }

    [Fact]
    public async Task Store_GetById_NullNotCached()
    {
        var inner = new Mock<IStoreService>();
        inner.Setup(s => s.GetStoreItemByIdAsync("missing")).ReturnsAsync((StoreItemDto?)null);
        var svc = new CachingStoreService(inner.Object, NewCache());

        await svc.GetStoreItemByIdAsync("missing");
        await svc.GetStoreItemByIdAsync("missing");

        inner.Verify(s => s.GetStoreItemByIdAsync("missing"), Times.Exactly(2));
    }

    // -------------------------------------------------------------------------
    // CachingForumService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Forum_GetSections_CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<IForumService>();
        inner.Setup(s => s.GetSectionsAsync()).ReturnsAsync(new List<ForumSectionDto> { new() });
        var svc = new CachingForumService(inner.Object, NewCache());

        await svc.GetSectionsAsync();
        await svc.GetSectionsAsync();

        inner.Verify(s => s.GetSectionsAsync(), Times.Once);
    }

    [Fact]
    public async Task Forum_GetTopics_CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<IForumService>();
        inner.Setup(s => s.GetTopicsAsync("sec1", It.IsAny<int>())).ReturnsAsync(new PagedResult<ForumTopicDto> { Items = new List<ForumTopicDto> { new() }, PageSize = 1 });
        var svc = new CachingForumService(inner.Object, NewCache());

        await svc.GetTopicsAsync("sec1");
        await svc.GetTopicsAsync("sec1");

        inner.Verify(s => s.GetTopicsAsync("sec1", It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task Forum_GetTopic_CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<IForumService>();
        inner.Setup(s => s.GetTopicByIdAsync("t1")).ReturnsAsync(new ForumTopicDto { Id = "t1" });
        var svc = new CachingForumService(inner.Object, NewCache());

        await svc.GetTopicByIdAsync("t1");
        await svc.GetTopicByIdAsync("t1");

        inner.Verify(s => s.GetTopicByIdAsync("t1"), Times.Once);
    }

    [Fact]
    public async Task Forum_GetReplies_CacheHit_DoesNotCallInner()
    {
        var inner = new Mock<IForumService>();
        inner.Setup(s => s.GetRepliesAsync("t1", It.IsAny<string>())).ReturnsAsync(new PagedResult<ForumReplyDto> { Items = new List<ForumReplyDto> { new() }, PageSize = 1 });
        var svc = new CachingForumService(inner.Object, NewCache());

        await svc.GetRepliesAsync("t1");
        await svc.GetRepliesAsync("t1");

        inner.Verify(s => s.GetRepliesAsync("t1", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Forum_CreateReply_InvalidatesTopicAndReplies()
    {
        var inner = new Mock<IForumService>();
        inner.Setup(s => s.GetTopicByIdAsync("t1")).ReturnsAsync(new ForumTopicDto { Id = "t1" });
        inner.Setup(s => s.GetRepliesAsync("t1", It.IsAny<string>())).ReturnsAsync(new PagedResult<ForumReplyDto> { Items = new List<ForumReplyDto>(), PageSize = 0 });
        inner.Setup(s => s.CreateReplyAsync("t1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
             .ReturnsAsync(new ForumReplyDto());
        var svc = new CachingForumService(inner.Object, NewCache());

        await svc.GetTopicByIdAsync("t1");  // populate topic cache
        await svc.GetRepliesAsync("t1");    // populate replies cache
        await svc.CreateReplyAsync("t1", "u1", "Alice", "Hello!"); // invalidates both
        await svc.GetTopicByIdAsync("t1");  // must re-fetch
        await svc.GetRepliesAsync("t1");    // must re-fetch

        inner.Verify(s => s.GetTopicByIdAsync("t1"), Times.Exactly(2));
        inner.Verify(s => s.GetRepliesAsync("t1", It.IsAny<string>()), Times.Exactly(2));
    }
}
