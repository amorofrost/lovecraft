using Lovecraft.Common.DTOs.Blog;
using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockBlogService : IBlogService
{
    public Task<List<BlogPostDto>> GetBlogPostsAsync()
    {
        return Task.FromResult(MockDataStore.BlogPosts);
    }

    public Task<BlogPostDto?> GetBlogPostByIdAsync(string postId)
    {
        var post = MockDataStore.BlogPosts.FirstOrDefault(p => p.Id == postId);
        return Task.FromResult(post);
    }
}
