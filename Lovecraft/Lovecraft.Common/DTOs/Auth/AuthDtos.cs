using System.Text.Json.Serialization;

namespace Lovecraft.Common.DTOs.Auth;

public class LoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string? InviteCode { get; set; }
}

public record RegistrationConfigDto(bool RequireEventInvite);

public class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public UserInfo User { get; set; } = new();
    public DateTime ExpiresAt { get; set; }
}

public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public List<string> AuthMethods { get; set; } = new();
}

public class RefreshTokenRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class VerifyEmailRequestDto
{
    public string Token { get; set; } = string.Empty;
}

public class ForgotPasswordRequestDto
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequestDto
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequestDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// Payload from the Telegram Login Widget (callback or redirect). Hash is verified server-side with the bot token.
/// </summary>
public class TelegramLoginRequestDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("photo_url")]
    public string? PhotoUrl { get; set; }

    [JsonPropertyName("auth_date")]
    public long AuthDate { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;
}

public class TelegramLoginConfigDto
{
    /// <summary>Bot username without @, for the widget data-telegram-login attribute.</summary>
    public string BotUsername { get; set; } = string.Empty;
}

/// <summary>Verified Telegram user identity carried across the pending-ticket flow.</summary>
public class TelegramUserInfoDto
{
    public long Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string? Username { get; set; }
    public string? PhotoUrl { get; set; }
}

/// <summary>Response from <c>/telegram-login</c>: either a full JWT pair, or a pending ticket for
/// a new Telegram identity that the user must attach to an account.</summary>
public class TelegramLoginResultDto
{
    /// <summary>"signedIn" | "pending"</summary>
    public string Status { get; set; } = string.Empty;
    public AuthResponseDto? Auth { get; set; }
    public string? Ticket { get; set; }
    public TelegramUserInfoDto? Telegram { get; set; }
}

/// <summary>Create a brand-new account from a verified Telegram identity (pending ticket).</summary>
public class TelegramRegisterRequestDto
{
    public string Ticket { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string? InviteCode { get; set; }
}

/// <summary>Link a verified Telegram identity to an existing email/password account in one shot.</summary>
public class TelegramLinkLoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Ticket { get; set; } = string.Empty;
}

/// <summary>Link a verified Telegram identity to the currently authenticated account.</summary>
public class TelegramLinkRequestDto
{
    public string Ticket { get; set; } = string.Empty;
}

/// <summary>Attach an email + password to a Telegram-only account. Finalized only after the user
/// clicks the verification link, at which point <c>local</c> is appended to AuthMethods.</summary>
public class AttachEmailRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class OAuthCallbackDto
{
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>Telegram Mini App <c>initData</c> string for Web App validation (not the Login Widget).</summary>
public class TelegramWebAppInitDataDto
{
    public string InitData { get; set; } = string.Empty;
}

/// <summary>Request body for <c>/auth/telegram-miniapp-login</c>: raw initData from <c>window.Telegram.WebApp</c>.</summary>
public class TelegramMiniAppLoginRequestDto
{
    public string InitData { get; set; } = string.Empty;
}

/// <summary>Response from Mini App login: <c>signedIn</c> (JWT pair) or <c>needsRegistration</c> (frontend prompts for profile/link).</summary>
public class TelegramMiniAppLoginResultDto
{
    /// <summary>"signedIn" | "needsRegistration"</summary>
    public string Status { get; set; } = string.Empty;
    public AuthResponseDto? Auth { get; set; }
    public TelegramUserInfoDto? Telegram { get; set; }
}

/// <summary>Create a new account from verified initData + profile fields + optional invite.</summary>
public class TelegramMiniAppRegisterRequestDto
{
    public string InitData { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string? InviteCode { get; set; }
}

/// <summary>Link verified initData to an existing email+password account in one call.</summary>
public class TelegramMiniAppLinkLoginRequestDto
{
    public string InitData { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LinkAuthMethodRequestDto
{
    public string Provider { get; set; } = string.Empty;
    public string? ExternalToken { get; set; }
}

/// <summary>Verified Google identity (OpenID <c>sub</c> + profile claims) from an ID token.</summary>
public class GoogleUserInfoDto
{
    public string Sub { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PictureUrl { get; set; }
}

/// <summary>Public Web client ID for Google Identity Services (GSI) — not a secret.</summary>
public class GoogleAuthConfigDto
{
    public string ClientId { get; set; } = string.Empty;
}

/// <summary>Request body: Google Sign-In <c>JWT credential</c> (ID token).</summary>
public class GoogleLoginRequestDto
{
    public string IdToken { get; set; } = string.Empty;
}

/// <summary>
/// Result of <c>/auth/google-login</c>: <c>signedIn</c>, <c>pending</c> (new Google user, complete profile
/// at <c>/welcome/google</c>), or <c>emailConflict</c> (rare: existing account email bound to a different
/// Google identity). Other flows auto-link same email to local or Google.
/// </summary>
public class GoogleLoginResultDto
{
    public string Status { get; set; } = string.Empty;
    public AuthResponseDto? Auth { get; set; }
    public string? Ticket { get; set; }
    public GoogleUserInfoDto? Google { get; set; }
    public string? Message { get; set; }
}

/// <summary>Create a new account from a verified Google pending ticket + profile fields + optional invite.</summary>
public class GoogleRegisterRequestDto
{
    public string Ticket { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string? InviteCode { get; set; }
}

public class AuthMethodDto
{
    public string Provider { get; set; } = string.Empty;
    public DateTime LinkedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
}
