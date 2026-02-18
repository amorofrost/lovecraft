using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lovecraft.Common.DTOs.Auth;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Services;
using System.Security.Claims;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user with email/password
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register([FromBody] RegisterRequestDto request)
    {
        try
        {
            // Validate password requirements
            if (!IsValidPassword(request.Password, out var passwordError))
            {
                return BadRequest(ApiResponse<AuthResponseDto>.ErrorResponse("WEAK_PASSWORD", passwordError));
            }

            var result = await _authService.RegisterAsync(request);
            
            if (result == null)
            {
                return BadRequest(ApiResponse<AuthResponseDto>.ErrorResponse(
                    "REGISTRATION_FAILED",
                    "Email or username already exists"));
            }

            return Ok(ApiResponse<AuthResponseDto>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration");
            return StatusCode(500, ApiResponse<AuthResponseDto>.ErrorResponse(
                "INTERNAL_ERROR",
                "Registration failed"));
        }
    }

    /// <summary>
    /// Login with email/password
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login([FromBody] LoginRequestDto request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            
            if (result == null)
            {
                return Unauthorized(ApiResponse<AuthResponseDto>.ErrorResponse(
                    "INVALID_CREDENTIALS",
                    "Invalid email or password, or email not verified"));
            }

            // Set refresh token as HttpOnly cookie
            SetRefreshTokenCookie(result.RefreshToken);

            return Ok(ApiResponse<AuthResponseDto>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, ApiResponse<AuthResponseDto>.ErrorResponse(
                "INTERNAL_ERROR",
                "Login failed"));
        }
    }

    /// <summary>
    /// Logout current user
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> Logout()
    {
        try
        {
            var refreshToken = Request.Cookies["refreshToken"];
            if (!string.IsNullOrEmpty(refreshToken))
            {
                await _authService.RevokeRefreshTokenAsync(refreshToken);
            }

            // Clear refresh token cookie
            Response.Cookies.Delete("refreshToken");

            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse("INTERNAL_ERROR", "Logout failed"));
        }
    }

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> RefreshToken()
    {
        try
        {
            var refreshToken = Request.Cookies["refreshToken"];
            
            if (string.IsNullOrEmpty(refreshToken))
            {
                return Unauthorized(ApiResponse<AuthResponseDto>.ErrorResponse(
                    "NO_REFRESH_TOKEN",
                    "Refresh token not found"));
            }

            var result = await _authService.RefreshTokenAsync(refreshToken);
            
            if (result == null)
            {
                return Unauthorized(ApiResponse<AuthResponseDto>.ErrorResponse(
                    "INVALID_REFRESH_TOKEN",
                    "Invalid or expired refresh token"));
            }

            // Set new refresh token cookie
            SetRefreshTokenCookie(result.RefreshToken);

            return Ok(ApiResponse<AuthResponseDto>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return StatusCode(500, ApiResponse<AuthResponseDto>.ErrorResponse(
                "INTERNAL_ERROR",
                "Token refresh failed"));
        }
    }

    /// <summary>
    /// Get current authenticated user
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserInfo>>> GetCurrentUser()
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(ApiResponse<UserInfo>.ErrorResponse(
                    "UNAUTHORIZED",
                    "User not authenticated"));
            }

            var user = await _authService.GetCurrentUserAsync(userId);
            
            if (user == null)
            {
                return NotFound(ApiResponse<UserInfo>.ErrorResponse("USER_NOT_FOUND", "User not found"));
            }

            return Ok(ApiResponse<UserInfo>.SuccessResponse(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user");
            return StatusCode(500, ApiResponse<UserInfo>.ErrorResponse(
                "INTERNAL_ERROR",
                "Failed to get user"));
        }
    }

    /// <summary>
    /// Verify email with token
    /// </summary>
    [HttpGet("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<bool>>> VerifyEmail([FromQuery] string token)
    {
        try
        {
            var result = await _authService.VerifyEmailAsync(token);
            
            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse(
                    "INVALID_TOKEN",
                    "Invalid or expired verification token"));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying email");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse(
                "INTERNAL_ERROR",
                "Email verification failed"));
        }
    }

    /// <summary>
    /// Resend email verification
    /// </summary>
    [HttpPost("resend-verification")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> ResendVerification()
    {
        // Mock: always return success
        return Ok(ApiResponse<bool>.SuccessResponse(true));
    }

    /// <summary>
    /// Request password reset
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<bool>>> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
    {
        try
        {
            await _authService.ForgotPasswordAsync(request.Email);
            
            // Always return success to not reveal if email exists
            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in forgot password");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse(
                "INTERNAL_ERROR",
                "Password reset request failed"));
        }
    }

    /// <summary>
    /// Reset password with token
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<bool>>> ResetPassword([FromBody] ResetPasswordRequestDto request)
    {
        try
        {
            if (!IsValidPassword(request.NewPassword, out var passwordError))
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("WEAK_PASSWORD", passwordError));
            }

            var result = await _authService.ResetPasswordAsync(request.Token, request.NewPassword);
            
            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse(
                    "INVALID_TOKEN",
                    "Invalid or expired reset token"));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse(
                "INTERNAL_ERROR",
                "Password reset failed"));
        }
    }

    /// <summary>
    /// Change password (requires current password)
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> ChangePassword([FromBody] ChangePasswordRequestDto request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(ApiResponse<bool>.ErrorResponse(
                    "UNAUTHORIZED",
                    "User not authenticated"));
            }

            if (!IsValidPassword(request.NewPassword, out var passwordError))
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("WEAK_PASSWORD", passwordError));
            }

            var result = await _authService.ChangePasswordAsync(
                userId, 
                request.CurrentPassword, 
                request.NewPassword);
            
            if (!result)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse(
                    "INVALID_PASSWORD",
                    "Current password is incorrect"));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse(
                "INTERNAL_ERROR",
                "Password change failed"));
        }
    }

    /// <summary>
    /// Get linked authentication methods
    /// </summary>
    [HttpGet("methods")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<AuthMethodDto>>>> GetAuthMethods()
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(ApiResponse<List<AuthMethodDto>>.ErrorResponse(
                    "UNAUTHORIZED",
                    "User not authenticated"));
            }

            var methods = await _authService.GetAuthMethodsAsync(userId);
            return Ok(ApiResponse<List<AuthMethodDto>>.SuccessResponse(methods));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting auth methods");
            return StatusCode(500, ApiResponse<List<AuthMethodDto>>.ErrorResponse(
                "INTERNAL_ERROR",
                "Failed to get authentication methods"));
        }
    }

    private void SetRefreshTokenCookie(string refreshToken)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // HTTPS only
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        };

        Response.Cookies.Append("refreshToken", refreshToken, cookieOptions);
    }

    private bool IsValidPassword(string password, out string error)
    {
        error = string.Empty;

        if (password.Length < 8)
        {
            error = "Password must be at least 8 characters";
            return false;
        }

        if (!password.Any(char.IsUpper))
        {
            error = "Password must contain at least one uppercase letter";
            return false;
        }

        if (!password.Any(char.IsLower))
        {
            error = "Password must contain at least one lowercase letter";
            return false;
        }

        if (!password.Any(char.IsDigit))
        {
            error = "Password must contain at least one number";
            return false;
        }

        if (!password.Any(ch => "!@#$%^&*()_+-=[]{}|;:,.<>?".Contains(ch)))
        {
            error = "Password must contain at least one special character";
            return false;
        }

        return true;
    }
}
