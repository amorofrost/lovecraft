using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Hubs;
using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Auth;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // Require authentication
public class ForumController : ControllerBase
{
    private readonly IForumService _forumService;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<ForumController> _logger;
    private readonly IAppConfigService _appConfig;
    private readonly IUserService _userService;

    public ForumController(
        IForumService forumService,
        IHubContext<ChatHub> hubContext,
        ILogger<ForumController> logger,
        IAppConfigService appConfig,
        IUserService userService)
    {
        _forumService = forumService;
        _hubContext = hubContext;
        _logger = logger;
        _appConfig = appConfig;
        _userService = userService;
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
            var section = (await _forumService.GetSectionsAsync()).FirstOrDefault(s => s.Id == sectionId);
            if (section is null)
                return NotFound(ApiResponse<List<ForumTopicDto>>.ErrorResponse("NOT_FOUND", "Section not found"));

            var allowed = await PermissionGuard.MeetsAsync(User, _userService, section.MinRank);
            if (!allowed)
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<List<ForumTopicDto>>.ErrorResponse(
                        AuthorizationErrors.InsufficientRank, AuthorizationErrors.InsufficientRankMessage));

            var topics = await _forumService.GetTopicsAsync(sectionId);
            var callerRank = await GetCallerRankAsync();
            if (callerRank == UserRank.Novice)
                topics = topics.Where(t => t.NoviceVisible).ToList();

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

            var allowed = await PermissionGuard.MeetsAsync(User, _userService, topic.MinRank);
            if (!allowed)
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<ForumTopicDto>.ErrorResponse(
                        AuthorizationErrors.InsufficientRank, AuthorizationErrors.InsufficientRankMessage));

            if (!topic.NoviceVisible && await GetCallerRankAsync() == UserRank.Novice)
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<ForumTopicDto>.ErrorResponse(
                        AuthorizationErrors.InsufficientRank, AuthorizationErrors.InsufficientRankMessage));

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
    /// Create a new topic in a section
    /// </summary>
    [HttpPost("sections/{sectionId}/topics")]
    public async Task<IActionResult> CreateTopic(
        string sectionId, [FromBody] CreateTopicRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ApiResponse<ForumTopicDto>.ErrorResponse(
                "VALIDATION_ERROR", "Validation failed"));

        if (HtmlGuard.ContainsHtml(request.Title))
            return BadRequest(ApiResponse<ForumTopicDto>.ErrorResponse("HTML_NOT_ALLOWED", "HTML tags are not permitted in topic title"));
        if (HtmlGuard.ContainsHtml(request.Content))
            return BadRequest(ApiResponse<ForumTopicDto>.ErrorResponse("HTML_NOT_ALLOWED", "HTML tags are not permitted in topic content"));

        var config = await _appConfig.GetConfigAsync();
        var allowed = await PermissionGuard.MeetsAsync(User, _userService, config.Permissions.CreateTopic);
        if (!allowed)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<ForumTopicDto>.ErrorResponse(
                    AuthorizationErrors.InsufficientRank,
                    AuthorizationErrors.InsufficientRankMessage));

        var authorId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var authorName = User.FindFirst(ClaimTypes.Name)?.Value;

        try
        {
            var result = await _forumService.CreateTopicAsync(
                sectionId, authorId!, authorName!, request.Title, request.Content,
                request.NoviceVisible, request.NoviceCanReply);
            return Ok(ApiResponse<ForumTopicDto>.SuccessResponse(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<ForumTopicDto>.ErrorResponse(
                "NOT_FOUND", "Section not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating topic in section {SectionId}", sectionId);
            return StatusCode(500, ApiResponse<ForumTopicDto>.ErrorResponse(
                "INTERNAL_ERROR", "An error occurred while creating the topic"));
        }
    }

    /// <summary>
    /// Post a reply to a topic
    /// </summary>
    [HttpPost("topics/{topicId}/replies")]
    public async Task<ActionResult<ApiResponse<ForumReplyDto>>> CreateReply(string topicId, [FromBody] CreateReplyRequestDto request)
    {
        var authorId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var authorName = User.FindFirst(ClaimTypes.Name)?.Value;

        if (HtmlGuard.ContainsHtml(request.Content))
            return BadRequest(ApiResponse<ForumReplyDto>.ErrorResponse("HTML_NOT_ALLOWED", "HTML tags are not permitted in reply content"));

        var topic = await _forumService.GetTopicByIdAsync(topicId);
        if (topic is null)
            return NotFound(ApiResponse<ForumReplyDto>.ErrorResponse("NOT_FOUND", "Topic not found"));

        var section = (await _forumService.GetSectionsAsync()).FirstOrDefault(s => s.Id == topic.SectionId);
        var sectionAllowed = section is null ||
            await PermissionGuard.MeetsAsync(User, _userService, section.MinRank);
        var topicAllowed = await PermissionGuard.MeetsAsync(User, _userService, topic.MinRank);
        if (!sectionAllowed || !topicAllowed)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<ForumReplyDto>.ErrorResponse(
                    AuthorizationErrors.InsufficientRank, AuthorizationErrors.InsufficientRankMessage));

        if (!topic.NoviceCanReply && await GetCallerRankAsync() == UserRank.Novice)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<ForumReplyDto>.ErrorResponse(
                    AuthorizationErrors.InsufficientRank, AuthorizationErrors.InsufficientRankMessage));

        try
        {
            var reply = await _forumService.CreateReplyAsync(topicId, authorId!, authorName!, request.Content, request.ImageUrls);
            await _hubContext.Clients.Group($"topic-{topicId}").SendAsync("ReplyPosted", reply, topicId);
            return Ok(ApiResponse<ForumReplyDto>.SuccessResponse(reply));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating reply for topic {TopicId}", topicId);
            return StatusCode(500, ApiResponse<ForumReplyDto>.ErrorResponse("INTERNAL_ERROR", "Failed to create reply"));
        }
    }

    [HttpPut("topics/{topicId}")]
    public async Task<IActionResult> UpdateTopic(string topicId, [FromBody] UpdateTopicRequestDto request)
    {
        var topic = await _forumService.GetTopicByIdAsync(topicId);
        if (topic is null)
            return NotFound(ApiResponse<ForumTopicDto>.ErrorResponse("NOT_FOUND", "Topic not found"));

        var callerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var staffRole = User.FindFirst("staffRole")?.Value ?? "none";
        var isAuthor = callerId == topic.AuthorId;
        var isModerator = EffectiveLevel.Parse(staffRole) >= EffectiveLevel.Moderator;

        if (!isAuthor && !isModerator)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<ForumTopicDto>.ErrorResponse(
                    AuthorizationErrors.InsufficientRank, AuthorizationErrors.InsufficientRankMessage));

        if ((request.IsPinned.HasValue || request.IsLocked.HasValue) && !isModerator)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<ForumTopicDto>.ErrorResponse(
                    AuthorizationErrors.ModeratorRequired, AuthorizationErrors.ModeratorRequiredMessage));

        var updated = await _forumService.UpdateTopicAsync(topicId, request);
        return Ok(ApiResponse<ForumTopicDto>.SuccessResponse(updated!));
    }

    private async Task<UserRank> GetCallerRankAsync()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(id)) return UserRank.Novice;
        var user = await _userService.GetUserByIdAsync(id);
        return user?.Rank ?? UserRank.Novice;
    }
}
