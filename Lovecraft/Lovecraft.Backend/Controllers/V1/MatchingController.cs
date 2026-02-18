using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DTOs.Matching;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Services;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // Require authentication
public class MatchingController : ControllerBase
{
    private readonly IMatchingService _matchingService;
    private readonly ILogger<MatchingController> _logger;

    public MatchingController(IMatchingService matchingService, ILogger<MatchingController> logger)
    {
        _matchingService = matchingService;
        _logger = logger;
    }

    /// <summary>
    /// Send a like to another user
    /// </summary>
    [HttpPost("likes")]
    public async Task<ActionResult<ApiResponse<LikeResponseDto>>> CreateLike([FromBody] CreateLikeRequestDto request)
    {
        try
        {
            const string currentUserId = "current-user";
            var result = await _matchingService.CreateLikeAsync(currentUserId, request.ToUserId);
            return Ok(ApiResponse<LikeResponseDto>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating like");
            return StatusCode(500, ApiResponse<LikeResponseDto>.ErrorResponse("INTERNAL_ERROR", "Failed to create like"));
        }
    }

    /// <summary>
    /// Get likes sent by current user
    /// </summary>
    [HttpGet("likes/sent")]
    public async Task<ActionResult<ApiResponse<List<LikeDto>>>> GetSentLikes()
    {
        try
        {
            const string currentUserId = "current-user";
            var likes = await _matchingService.GetSentLikesAsync(currentUserId);
            return Ok(ApiResponse<List<LikeDto>>.SuccessResponse(likes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sent likes");
            return StatusCode(500, ApiResponse<List<LikeDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get sent likes"));
        }
    }

    /// <summary>
    /// Get likes received by current user
    /// </summary>
    [HttpGet("likes/received")]
    public async Task<ActionResult<ApiResponse<List<LikeDto>>>> GetReceivedLikes()
    {
        try
        {
            const string currentUserId = "current-user";
            var likes = await _matchingService.GetReceivedLikesAsync(currentUserId);
            return Ok(ApiResponse<List<LikeDto>>.SuccessResponse(likes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting received likes");
            return StatusCode(500, ApiResponse<List<LikeDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get received likes"));
        }
    }

    /// <summary>
    /// Get all matches for current user
    /// </summary>
    [HttpGet("matches")]
    public async Task<ActionResult<ApiResponse<List<MatchDto>>>> GetMatches()
    {
        try
        {
            const string currentUserId = "current-user";
            var matches = await _matchingService.GetMatchesAsync(currentUserId);
            return Ok(ApiResponse<List<MatchDto>>.SuccessResponse(matches));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting matches");
            return StatusCode(500, ApiResponse<List<MatchDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get matches"));
        }
    }
}
