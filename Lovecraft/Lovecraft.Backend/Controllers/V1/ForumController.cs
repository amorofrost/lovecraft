using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Services;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // Require authentication
public class ForumController : ControllerBase
{
    private readonly IForumService _forumService;
    private readonly ILogger<ForumController> _logger;

    public ForumController(IForumService forumService, ILogger<ForumController> logger)
    {
        _forumService = forumService;
        _logger = logger;
    }

    /// <summary>
    /// Get all forum sections
    /// </summary>
    [HttpGet("sections")]
    public async Task<ActionResult<ApiResponse<List<ForumSectionDto>>>> GetSections()
    {
        try
        {
            var sections = await _forumService.GetSectionsAsync();
            return Ok(ApiResponse<List<ForumSectionDto>>.SuccessResponse(sections));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting forum sections");
            return StatusCode(500, ApiResponse<List<ForumSectionDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get forum sections"));
        }
    }

    /// <summary>
    /// Get topics in a section
    /// </summary>
    [HttpGet("sections/{sectionId}/topics")]
    public async Task<ActionResult<ApiResponse<List<ForumTopicDto>>>> GetTopics(string sectionId)
    {
        try
        {
            var topics = await _forumService.GetTopicsAsync(sectionId);
            return Ok(ApiResponse<List<ForumTopicDto>>.SuccessResponse(topics));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting topics for section {SectionId}", sectionId);
            return StatusCode(500, ApiResponse<List<ForumTopicDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get topics"));
        }
    }
}
