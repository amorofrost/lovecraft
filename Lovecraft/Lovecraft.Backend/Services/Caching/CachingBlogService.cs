using Microsoft.Extensions.Caching.Memory;
using Lovecraft.Common.DTOs.Blog;

namespace Lovecraft.Backend.Services.Caching;

/// <summary>
/// Caching decorator for IBlogService. Reads use a 5-minute TTL; writes invalidate
/// the all-posts cache and the affected post key.
/// </summary>
public class CachingBlogService : IBlogService
{
    private readonly IBlogService _inner;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const string AllKey = "blog:all";
    private static string PostKey(string id) => $"blog:{id}";

    public CachingBlogService(IBlogService inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<List<BlogPostDto>> GetBlogPostsAsync()
    {
        if (_cache.TryGetValue(AllKey, out List<BlogPostDto>? cached) && cached is not null)
            return cached;

        var result = await _inner.GetBlogPostsAsync();
        _cache.Set(AllKey, result, Ttl);
        return result;
    }

    public async Task<BlogPostDto?> GetBlogPostByIdAsync(string postId)
    {
        var key = PostKey(postId);
        if (_cache.TryGetValue(key, out BlogPostDto? cached))
            return cached;

        var result = await _inner.GetBlogPostByIdAsync(postId);
        if (result is not null)
            _cache.Set(key, result, Ttl);
        return result;
    }

    public async Task<BlogPostDto> CreateBlogPostAsync(BlogPostDto post)
    {
        var r = await _inner.CreateBlogPostAsync(post);
        _cache.Remove(AllKey);
        _cache.Remove(PostKey(r.Id));
        return r;
    }

    public async Task<BlogPostDto?> UpdateBlogPostAsync(string postId, BlogPostDto post)
    {
        var r = await _inner.UpdateBlogPostAsync(postId, post);
        _cache.Remove(AllKey);
        _cache.Remove(PostKey(postId));
        return r;
    }

    public async Task<bool> DeleteBlogPostAsync(string postId)
    {
        var ok = await _inner.DeleteBlogPostAsync(postId);
        if (ok)
        {
            _cache.Remove(AllKey);
            _cache.Remove(PostKey(postId));
        }
        return ok;
    }
}
