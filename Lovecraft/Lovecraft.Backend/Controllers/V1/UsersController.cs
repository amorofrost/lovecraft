using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Constants;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Helpers;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // Require authentication for all endpoints
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IEventService _eventService;
    private readonly ILogger<UsersController> _logger;
    private readonly IImageService _imageService;

    public UsersController(
        IUserService userService,
        IEventService eventService,
        ILogger<UsersController> logger,
        IImageService imageService)
    {
        _userService = userService;
        _eventService = eventService;
        _logger = logger;
        _imageService = imageService;
    }

    /// <summary>
    /// Get list of users (for search/matching)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetUsers([FromQuery] int skip = 0, [FromQuery] int take = 10)
    {
        try
        {
            var users = await _userService.GetUsersAsync(skip, take);
            await Task.WhenAll(users.Select(async u =>
            {
                var attended = await _eventService.GetEventsAttendedByUserAsync(u.Id);
                u.AttendedEvents = attended.Select(StripEventForProfile).ToList();
            }));
            return Ok(ApiResponse<List<UserDto>>.SuccessResponse(users));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users");
            return StatusCode(500, ApiResponse<List<UserDto>>.ErrorResponse("INTERNAL_ERROR", "Failed to get users"));
        }
    }

    /// <summary>
    /// Get user by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetUser(string id)
    {
        try
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(ApiResponse<UserDto>.ErrorResponse("NOT_FOUND", "User not found"));
            }

            var attended = await _eventService.GetEventsAttendedByUserAsync(id);
            user.AttendedEvents = attended.Select(StripEventForProfile).ToList();
            return Ok(ApiResponse<UserDto>.SuccessResponse(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user {UserId}", id);
            return StatusCode(500, ApiResponse<UserDto>.ErrorResponse("INTERNAL_ERROR", "Failed to get user"));
        }
    }

    /// <summary>
    /// Update user profile
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateUser(string id, [FromBody] UserDto user)
    {
        if (HtmlGuard.ContainsHtml(user.Name))
            return BadRequest(ApiResponse<UserDto>.ErrorResponse("HTML_NOT_ALLOWED", "HTML tags are not permitted in name"));
        if (HtmlGuard.ContainsHtml(user.Location))
            return BadRequest(ApiResponse<UserDto>.ErrorResponse("HTML_NOT_ALLOWED", "HTML tags are not permitted in location"));
        if (HtmlGuard.ContainsHtml(user.Bio))
            return BadRequest(ApiResponse<UserDto>.ErrorResponse("HTML_NOT_ALLOWED", "HTML tags are not permitted in bio"));

        if (user.Prompts is { } prompts)
        {
            if (prompts.Count > 3)
                return BadRequest(ApiResponse<UserDto>.ErrorResponse("PROMPTS_TOO_MANY", "At most 3 prompts allowed"));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in prompts)
            {
                if (!PromptIds.All.Contains(p.PromptId))
                    return BadRequest(ApiResponse<UserDto>.ErrorResponse("UNKNOWN_PROMPT_ID", $"Prompt id '{p.PromptId}' is not in the catalogue"));
                if (!seen.Add(p.PromptId))
                    return BadRequest(ApiResponse<UserDto>.ErrorResponse("DUPLICATE_PROMPT_ID", "A prompt id appears more than once"));
                if ((p.Answer ?? string.Empty).Length > 200)
                    return BadRequest(ApiResponse<UserDto>.ErrorResponse("PROMPT_ANSWER_TOO_LONG", "Prompt answer must be 200 characters or less"));
                if (HtmlGuard.ContainsHtml(p.Answer))
                    return BadRequest(ApiResponse<UserDto>.ErrorResponse("HTML_NOT_ALLOWED", "HTML tags are not permitted in prompt answers"));
            }
        }

        if (user.Images is { Count: > 6 })
            return BadRequest(ApiResponse<UserDto>.ErrorResponse("IMAGES_TOO_MANY", "At most 6 images allowed"));

        try
        {
            var updatedUser = await _userService.UpdateUserAsync(id, user);
            return Ok(ApiResponse<UserDto>.SuccessResponse(updatedUser));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", id);
            return StatusCode(500, ApiResponse<UserDto>.ErrorResponse("INTERNAL_ERROR", "Failed to update user"));
        }
    }

    [HttpPut("{id}/role")]
    [RequireStaffRole("admin")]
    public async Task<IActionResult> AssignRole(string id, [FromBody] AssignRoleRequestDto request)
    {
        await _userService.SetStaffRoleAsync(id, request.Role);
        return Ok(ApiResponse<object>.SuccessResponse(new { userId = id, staffRole = request.Role }));
    }

    [HttpPut("{id}/rank-override")]
    [RequireStaffRole("admin")]
    public async Task<IActionResult> SetRankOverride(
        string id, [FromBody] SetRankOverrideRequestDto request)
    {
        await _userService.SetRankOverrideAsync(id, request.Rank);
        return Ok(ApiResponse<object>.SuccessResponse(new { userId = id, rankOverride = request.Rank }));
    }

    /// <summary>
    /// Upload a profile image for a user
    /// </summary>
    [HttpPost("{id}/images")]
    public async Task<ActionResult<ApiResponse<string>>> UploadProfileImage(string id, IFormFile image)
    {
        var callerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(callerId) || callerId != id)
            return StatusCode(403, ApiResponse<string>.ErrorResponse("FORBIDDEN", "You can only upload your own profile image"));

        if (image == null)
            return BadRequest(ApiResponse<string>.ErrorResponse("INVALID_IMAGE", "Image file is required"));

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(image.ContentType))
            return BadRequest(ApiResponse<string>.ErrorResponse("INVALID_IMAGE_TYPE", "Accepted types: JPEG, PNG, WebP"));

        if (image.Length > 20 * 1024 * 1024)
            return BadRequest(ApiResponse<string>.ErrorResponse("IMAGE_TOO_LARGE", "Image must be 20 MB or less"));

        try
        {
            var url = await _imageService.UploadProfileImageAsync(id, image.OpenReadStream(), image.ContentType);
            return Ok(ApiResponse<string>.SuccessResponse(url));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading profile image for user {UserId}", id);
            return StatusCode(500, ApiResponse<string>.ErrorResponse("INTERNAL_ERROR", "Failed to upload image"));
        }
    }

    /// <summary>
    /// Public profile does not need full attendee lists; keeps payloads small.
    /// </summary>
    private static EventDto StripEventForProfile(EventDto e) => new()
    {
        Id = e.Id,
        Title = e.Title,
        Description = e.Description,
        ImageUrl = e.ImageUrl,
        BadgeImageUrl = e.BadgeImageUrl,
        Date = e.Date,
        EndDate = e.EndDate,
        Location = e.Location,
        Capacity = e.Capacity,
        Attendees = new List<string>(),
        Category = e.Category,
        Price = e.Price,
        Organizer = e.Organizer,
        ExternalUrl = e.ExternalUrl,
        IsSecret = e.IsSecret,
        Visibility = e.Visibility,
        ForumTopicId = e.ForumTopicId,
        Archived = e.Archived,
    };
}
