using System.ComponentModel.DataAnnotations;

namespace Lovecraft.Common.DTOs.Blog;

public class BlogPostDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public DateTime Date { get; set; }
}

/// <summary>Shared fields for create/update blog posts (admin).</summary>
public class BlogPostMutationDto
{
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(10_000)]
    public string Excerpt { get; set; } = string.Empty;

    [StringLength(500_000)]
    public string Content { get; set; } = string.Empty;

    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string ImageUrl { get; set; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Author { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = new();

    public DateTime Date { get; set; }
}

public class CreateBlogPostRequestDto : BlogPostMutationDto
{
    [Required]
    [RegularExpression(@"^[a-z0-9][a-z0-9_-]{0,62}$", ErrorMessage = "Id must be a lowercase slug (a-z, digits, hyphen, underscore).")]
    public string Id { get; set; } = string.Empty;
}
