using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Blog;

namespace Lovecraft.Backend.Services.Azure;

public class AzureBlogService : IBlogService
{
    private readonly TableClient _blogTable;
    private readonly ILogger<AzureBlogService> _logger;

    public AzureBlogService(TableServiceClient tableServiceClient, ILogger<AzureBlogService> logger)
    {
        _logger = logger;
        _blogTable = tableServiceClient.GetTableClient(TableNames.BlogPosts);
        _blogTable.CreateIfNotExistsAsync().GetAwaiter().GetResult();
    }

    public async Task<List<BlogPostDto>> GetBlogPostsAsync()
    {
        var results = new List<BlogPostDto>();
        // Results come back in RK order (reversed ticks = newest first)
        await foreach (var entity in _blogTable.QueryAsync<BlogPostEntity>(filter: "PartitionKey eq 'BLOG'"))
        {
            results.Add(ToDto(entity));
        }
        return results;
    }

    public async Task<BlogPostDto?> GetBlogPostByIdAsync(string postId)
    {
        var entity = await FindEntityByPostIdAsync(postId);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<BlogPostDto> CreateBlogPostAsync(BlogPostDto post)
    {
        if (await FindEntityByPostIdAsync(post.Id) is not null)
            throw new InvalidOperationException($"Blog post '{post.Id}' already exists.");

        var date = DateTime.SpecifyKind(post.Date, DateTimeKind.Utc);
        var entity = new BlogPostEntity
        {
            PartitionKey = "BLOG",
            RowKey = BlogPostEntity.BuildRowKey(date, post.Id),
            PostId = post.Id,
            Title = post.Title,
            Excerpt = post.Excerpt,
            Content = post.Content,
            ImageUrl = post.ImageUrl,
            Author = post.Author,
            TagsJson = JsonSerializer.Serialize(post.Tags ?? new List<string>()),
            Date = date,
        };
        await _blogTable.AddEntityAsync(entity);
        return ToDto(entity);
    }

    public async Task<BlogPostDto?> UpdateBlogPostAsync(string postId, BlogPostDto post)
    {
        var existing = await FindEntityByPostIdAsync(postId);
        if (existing is null)
            return null;

        var date = DateTime.SpecifyKind(post.Date, DateTimeKind.Utc);
        var newRowKey = BlogPostEntity.BuildRowKey(date, postId);

        if (!string.Equals(existing.RowKey, newRowKey, StringComparison.Ordinal))
        {
            await _blogTable.DeleteEntityAsync(existing.PartitionKey, existing.RowKey, ETag.All);
            existing.RowKey = newRowKey;
        }

        existing.PostId = postId;
        existing.Title = post.Title;
        existing.Excerpt = post.Excerpt;
        existing.Content = post.Content;
        existing.ImageUrl = post.ImageUrl;
        existing.Author = post.Author;
        existing.TagsJson = JsonSerializer.Serialize(post.Tags ?? new List<string>());
        existing.Date = date;

        await _blogTable.UpsertEntityAsync(existing, TableUpdateMode.Replace);
        return ToDto(existing);
    }

    public async Task<bool> DeleteBlogPostAsync(string postId)
    {
        var entity = await FindEntityByPostIdAsync(postId);
        if (entity is null)
            return false;
        await _blogTable.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, ETag.All);
        return true;
    }

    private async Task<BlogPostEntity?> FindEntityByPostIdAsync(string postId)
    {
        var escaped = ODataEscape(postId);
        await foreach (var entity in _blogTable.QueryAsync<BlogPostEntity>(
            filter: $"PartitionKey eq 'BLOG' and PostId eq '{escaped}'"))
        {
            return entity;
        }
        return null;
    }

    private static string ODataEscape(string s) => s.Replace("'", "''");

    private static BlogPostDto ToDto(BlogPostEntity entity)
    {
        List<string> tags;
        try { tags = JsonSerializer.Deserialize<List<string>>(entity.TagsJson) ?? new List<string>(); }
        catch { tags = new List<string>(); }

        return new BlogPostDto
        {
            Id = entity.PostId,
            Title = entity.Title,
            Excerpt = entity.Excerpt,
            Content = entity.Content,
            ImageUrl = entity.ImageUrl,
            Author = entity.Author,
            Tags = tags,
            Date = entity.Date
        };
    }
}
