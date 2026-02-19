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

    /// <summary>
    /// Get a single topic by ID
    /// </summary>
    [HttpGet("topics/{topicId}")]
    public async Task<ActionResult<ApiResponse<ForumTopicDto>>> GetTopic(string topicId)
    {
        try
        {
            var topic = await _forumService.GetTopicByIdAsync(topicId);
            if (topic == null)
                return NotFound(ApiResponse<ForumTopicDto>.ErrorResponse("NOT_FOUND", "Topic not found"));
            return Ok(ApiResponse<ForumTopicDto>.SuccessResponse(topic));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting topic {TopicId}", topicId);
            return StatusCode(500, ApiResponse<ForumTopicDto>.ErrorResponse("INTERNAL_ERROR", "Failed to get topic"));
        }
    }

    /// <summary>
    /// Get all replies for a topic
    /// </summary>
    [HttpGet("topics/{topicId}/replies")]
    public async Task<ActionResult<ApiResponse<List<ForumReplyDto>>>> GetReplies(string topicId)
    {
        try
        {
            var replies = await _forumService.GetRepliesAsync(topicId);
            return Ok(ApiResponse<List<ForumReplyDto>>.SuccessResponse(replies));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting replies for topic {TopicId}", topicId);
            return StatusCode(500, ApiResponse<List<ForumReplyDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get replies"));
        }
    }

    /// <summary>
    /// Post a reply to a topic
    /// </summary>
    [HttpPost("topics/{topicId}/replies")]
    public async Task<ActionResult<ApiResponse<ForumReplyDto>>> CreateReply(string topicId, [FromBody] CreateReplyRequestDto request)
    {
        try
        {
            const string currentUserId = "current-user";
            const string currentUserName = "Вы";
            var reply = await _forumService.CreateReplyAsync(topicId, currentUserId, currentUserName, request.Content);
            return Ok(ApiResponse<ForumReplyDto>.SuccessResponse(reply));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating reply for topic {TopicId}", topicId);
            return StatusCode(500, ApiResponse<ForumReplyDto>.ErrorResponse("INTERNAL_ERROR", "Failed to create reply"));
        }
    }
}
