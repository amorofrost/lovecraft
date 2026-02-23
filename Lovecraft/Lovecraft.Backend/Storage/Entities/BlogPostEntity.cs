using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

public class BlogPostEntity : ITableEntity
{
    // PK = "BLOG", RK = {reversedTicks}#{postId}
    public string PartitionKey { get; set; } = "BLOG";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string PostId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string TagsJson { get; set; } = "[]";
    public DateTime Date { get; set; }

    public static string BuildRowKey(DateTime createdAt, string postId)
    {
        var reversedTicks = (DateTime.MaxValue.Ticks - createdAt.Ticks).ToString("D19");
        return $"{reversedTicks}-{postId}";
    }
}
