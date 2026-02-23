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
        // Need to scan because we don't know the reversed-ticks portion of the RK
        await foreach (var entity in _blogTable.QueryAsync<BlogPostEntity>(
            filter: $"PartitionKey eq 'BLOG' and PostId eq '{postId}'"))
        {
            return ToDto(entity);
        }
        return null;
    }

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
