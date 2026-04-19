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

    public Task<BlogPostDto> CreateBlogPostAsync(BlogPostDto post)
    {
        if (MockDataStore.BlogPosts.Any(p => p.Id == post.Id))
            throw new InvalidOperationException($"Blog post '{post.Id}' already exists.");
        MockDataStore.BlogPosts.Add(post);
        return Task.FromResult(post);
    }

    public Task<BlogPostDto?> UpdateBlogPostAsync(string postId, BlogPostDto post)
    {
        var existing = MockDataStore.BlogPosts.FirstOrDefault(p => p.Id == postId);
        if (existing is null)
            return Task.FromResult<BlogPostDto?>(null);
        existing.Title = post.Title;
        existing.Excerpt = post.Excerpt;
        existing.Content = post.Content;
        existing.ImageUrl = post.ImageUrl;
        existing.Author = post.Author;
        existing.Tags = new List<string>(post.Tags ?? new List<string>());
        existing.Date = post.Date;
        return Task.FromResult<BlogPostDto?>(existing);
    }

    public Task<bool> DeleteBlogPostAsync(string postId)
    {
        var ix = MockDataStore.BlogPosts.FindIndex(p => p.Id == postId);
        if (ix < 0)
            return Task.FromResult(false);
        MockDataStore.BlogPosts.RemoveAt(ix);
        return Task.FromResult(true);
    }
}
