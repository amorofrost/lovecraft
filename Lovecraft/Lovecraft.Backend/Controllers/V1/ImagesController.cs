using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Images;
using Lovecraft.Common.Models;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/images")]
public class ImagesController : ControllerBase
{
    private readonly IImageService _imageService;
    private readonly ILogger<ImagesController> _logger;

    private static readonly HashSet<string> AllowedContentTypes = new()
    {
        "image/jpeg", "image/png", "image/gif", "image/webp"
    };

    private string CurrentUserId =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "current-user";

    public ImagesController(IImageService imageService, ILogger<ImagesController> logger)
    {
        _imageService = imageService;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024 + 1024)]
    public async Task<IActionResult> UploadContentImage([FromForm] IFormFile file)
    {
        if (!AllowedContentTypes.Contains(file.ContentType))
            return BadRequest(ApiResponse<UploadImageResponseDto>.ErrorResponse(
                "INVALID_CONTENT_TYPE",
                "Only JPEG, PNG, GIF, and WebP images are allowed."));

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(ApiResponse<UploadImageResponseDto>.ErrorResponse(
                "FILE_TOO_LARGE",
                "Image must be 10 MB or less."));

        try
        {
            var url = await _imageService.UploadContentImageAsync(
                CurrentUserId, file.OpenReadStream(), file.ContentType);
            return Ok(ApiResponse<UploadImageResponseDto>.SuccessResponse(
                new UploadImageResponseDto { Url = url }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content image upload failed for user {UserId}", CurrentUserId);
            return StatusCode(500, ApiResponse<UploadImageResponseDto>.ErrorResponse(
                "UPLOAD_FAILED", "Image upload failed. Please try again."));
        }
    }
}
