using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DTOs.Blog;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Services;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // Require authentication
public class BlogController : ControllerBase
{
    private readonly IBlogService _blogService;
    private readonly ILogger<BlogController> _logger;

    public BlogController(IBlogService blogService, ILogger<BlogController> logger)
    {
        _blogService = blogService;
        _logger = logger;
    }

    /// <summary>
    /// Get all blog posts
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<BlogPostDto>>>> GetBlogPosts()
    {
        try
        {
            var posts = await _blogService.GetBlogPostsAsync();
            return Ok(ApiResponse<List<BlogPostDto>>.SuccessResponse(posts));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting blog posts");
            return StatusCode(500, ApiResponse<List<BlogPostDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get blog posts"));
        }
    }

    /// <summary>
    /// Get blog post by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<BlogPostDto>>> GetBlogPost(string id)
    {
        try
        {
            var post = await _blogService.GetBlogPostByIdAsync(id);
            if (post == null)
            {
                return NotFound(ApiResponse<BlogPostDto>.ErrorResponse("NOT_FOUND", "Blog post not found"));
            }
            return Ok(ApiResponse<BlogPostDto>.SuccessResponse(post));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting blog post {PostId}", id);
            return StatusCode(500, ApiResponse<BlogPostDto>.ErrorResponse("INTERNAL_ERROR", "Failed to get blog post"));
        }
    }
}
