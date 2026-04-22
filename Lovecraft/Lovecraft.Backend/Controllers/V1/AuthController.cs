using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Lovecraft.Common.DTOs.Auth;
using Lovecraft.Common.Models;
using Lovecraft.Backend.Configuration;
using Lovecraft.Backend.Services;
using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly IAppConfigService _appConfig;
    private readonly TelegramAuthOptions _telegramAuth;

    public AuthController(
        IAuthService authService,
        ILogger<AuthController> logger,
        IAppConfigService appConfig,
        IOptions<TelegramAuthOptions> telegramAuth)
    {
        _authService = authService;
        _logger = logger;
        _appConfig = appConfig;
        _telegramAuth = telegramAuth.Value;
    }

    /// <summary>Public config for the Telegram Login Widget (bot username). No secrets.</summary>
    [HttpGet("telegram-login-config")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public ActionResult<ApiResponse<TelegramLoginConfigDto>> GetTelegramLoginConfig()
    {
        var username = _telegramAuth.BotUsername?.Trim() ?? "";
        return Ok(ApiResponse<TelegramLoginConfigDto>.SuccessResponse(
            new TelegramLoginConfigDto { BotUsername = username }));
    }

    /// <summary>
    /// Verify Telegram Login Widget payload. Returns <c>signedIn</c> with a JWT pair for a known
    /// Telegram id, or <c>pending</c> with a short-lived ticket for a new Telegram identity —
    /// the frontend then routes the user to <c>/welcome/telegram</c> to either link an existing
    /// email account or create a new one. No account is written on the pending path.
    /// </summary>
    [HttpPost("telegram-login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<ActionResult<ApiResponse<TelegramLoginResultDto>>> TelegramLogin([FromBody] TelegramLoginRequestDto request)
    {
        try
        {
            var result = await _authService.TelegramLoginAsync(request);
            if (result == null)
            {
                return BadRequest(ApiResponse<TelegramLoginResultDto>.ErrorResponse(
                    "TELEGRAM_AUTH_FAILED",
                    "Telegram login failed. Ensure the bot token matches BotFather and the widget data is fresh."));
            }

            if (result.Status == "signedIn" && result.Auth is not null)
                SetRefreshTokenCookie(result.Auth.RefreshToken);

            return Ok(ApiResponse<TelegramLoginResultDto>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram login error");
            return StatusCode(500, ApiResponse<TelegramLoginResultDto>.ErrorResponse(
                "INTERNAL_ERROR",
                "Telegram login failed"));
        }
    }

    /// <summary>Create a new account from a verified Telegram pending ticket + profile fields + optional invite code.</summary>
    [HttpPost("telegram-register")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> TelegramRegister([FromBody] TelegramRegisterRequestDto request)
    {
        try
        {
            var result = await _authService.TelegramRegisterAsync(request);
            if (result == null)
            {
                return BadRequest(ApiResponse<AuthResponseDto>.ErrorResponse(
                    "TELEGRAM_REGISTER_FAILED",
                    "Telegram registration failed. The pending ticket may have expired or the Telegram id is already linked."));
            }

            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(ApiResponse<AuthResponseDto>.SuccessResponse(result));
        }
        catch (InvalidInviteCodeException)
        {
            return BadRequest(ApiResponse<AuthResponseDto>.ErrorResponse("INVALID_INVITE_CODE", "Invalid invite code"));
        }
        catch (InviteRequiredException)
        {
            return BadRequest(ApiResponse<AuthResponseDto>.ErrorResponse(
                "INVITE_REQUIRED",
                "Event invite code is required for registration"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram register error");
            return StatusCode(500, ApiResponse<AuthResponseDto>.ErrorResponse("INTERNAL_ERROR", "Telegram registration failed"));
        }
    }

    /// <summary>Link a verified Telegram pending ticket to an existing email+password account in one call.</summary>
    [HttpPost("telegram-link-login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> TelegramLinkLogin([FromBody] TelegramLinkLoginRequestDto request)
    {
        try
        {
            var result = await _authService.TelegramLinkLoginAsync(request);
            if (result == null)
            {
                return BadRequest(ApiResponse<AuthResponseDto>.ErrorResponse(
                    "TELEGRAM_LINK_LOGIN_FAILED",
                    "Could not link Telegram. Ticket may be expired, credentials may be wrong, or the Telegram id is already linked elsewhere."));
            }

            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(ApiResponse<AuthResponseDto>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram link-login error");
            return StatusCode(500, ApiResponse<AuthResponseDto>.ErrorResponse("INTERNAL_ERROR", "Telegram link failed"));
        }
    }

    /// <summary>Link a verified Telegram pending ticket to the currently authenticated account.</summary>
    [HttpPost("telegram-link")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> TelegramLink([FromBody] TelegramLinkRequestDto request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<AuthResponseDto>.ErrorResponse("UNAUTHORIZED", "User not authenticated"));

            var result = await _authService.TelegramLinkAsync(userId, request.Ticket);
            if (result == null)
            {
                return BadRequest(ApiResponse<AuthResponseDto>.ErrorResponse(
                    "TELEGRAM_LINK_FAILED",
                    "Could not link Telegram. Ticket may be expired or the Telegram id is already linked to a different account."));
            }

            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(ApiResponse<AuthResponseDto>.SuccessResponse(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram link error");
            return StatusCode(500, ApiResponse<AuthResponseDto>.ErrorResponse("INTERNAL_ERROR", "Telegram link failed"));
        }
    }

    /// <summary>
    /// Request an email+password attachment for a Telegram-only account. Sends a verification email;
    /// email and password are only applied to the user (and <c>local</c> added to AuthMethods) when
    /// the verification link is clicked.
    /// </summary>
    [HttpPost("attach-email")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> AttachEmail([FromBody] AttachEmailRequestDto request)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<bool>.ErrorResponse("UNAUTHORIZED", "User not authenticated"));

            if (!IsValidPassword(request.Password, out var passwordError))
                return BadRequest(ApiResponse<bool>.ErrorResponse("WEAK_PASSWORD", passwordError));

            var result = await _authService.RequestEmailAttachAsync(userId, request.Email, request.Password);
            return result switch
            {
                AttachEmailResult.Ok => Ok(ApiResponse<bool>.SuccessResponse(true)),
                AttachEmailResult.UserNotFound => NotFound(ApiResponse<bool>.ErrorResponse("USER_NOT_FOUND", "User not found")),
                AttachEmailResult.EmailAlreadyTaken => BadRequest(ApiResponse<bool>.ErrorResponse("EMAIL_TAKEN", "Email already in use")),
                AttachEmailResult.AlreadyHasLocal => BadRequest(ApiResponse<bool>.ErrorResponse("ALREADY_HAS_LOCAL", "Account already has an email login")),
                AttachEmailResult.ReservedDomain => BadRequest(ApiResponse<bool>.ErrorResponse("RESERVED_DOMAIN", "That email domain is reserved")),
                _ => StatusCode(500, ApiResponse<bool>.ErrorResponse("INTERNAL_ERROR", "Attach-email failed"))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Attach-email error");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse("INTERNAL_ERROR", "Attach-email failed"));
        }
    }

    /// <summary>
    /// Register a new user with email/password
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
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
        catch (InvalidInviteCodeException)
        {
            return BadRequest(ApiResponse<AuthResponseDto>.ErrorResponse(
                "INVALID_INVITE_CODE",
                "Invalid invite code"));
        }
        catch (InviteRequiredException)
        {
            return BadRequest(ApiResponse<AuthResponseDto>.ErrorResponse(
                "INVITE_REQUIRED",
                "Event invite code is required for registration"));
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
    /// Returns whether a valid event invite code is required for new account registration (see appconfig partition <c>registration</c>).
    /// </summary>
    [HttpGet("registration-config")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<RegistrationConfigDto>>> GetRegistrationConfig()
    {
        var cfg = await _appConfig.GetConfigAsync();
        return Ok(ApiResponse<RegistrationConfigDto>.SuccessResponse(
            new RegistrationConfigDto(cfg.Registration.RequireEventInvite)));
    }

    /// <summary>
    /// Login with email/password
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRateLimit")]
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
    /// Refresh access token using refresh token.
    /// Accepts the token from the request body (localStorage flow) or an HttpOnly cookie.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> RefreshToken(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
        RefreshTokenRequestDto? request)
    {
        try
        {
            // Prefer the token supplied in the request body (works over HTTP);
            // fall back to the HttpOnly cookie (works over HTTPS).
            var refreshToken = request?.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken))
                refreshToken = Request.Cookies["refreshToken"];

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
    // TODO: add [EnableRateLimiting("AuthRateLimit")] when the email service is real (currently a mock stub)
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
    [EnableRateLimiting("AuthRateLimit")]
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
    [EnableRateLimiting("AuthRateLimit")]
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
            // Only set the Secure flag when the connection is already HTTPS.
            // This allows the cookie mechanism to work in HTTP dev/staging environments.
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
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
