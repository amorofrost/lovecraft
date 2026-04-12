using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Helpers;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // Require authentication for all endpoints
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;
    private readonly IImageService _imageService;

    public UsersController(IUserService userService, ILogger<UsersController> logger, IImageService imageService)
    {
        _userService = userService;
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

        if (image.Length > 5 * 1024 * 1024)
            return BadRequest(ApiResponse<string>.ErrorResponse("IMAGE_TOO_LARGE", "Image must be 5 MB or less"));

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
}
