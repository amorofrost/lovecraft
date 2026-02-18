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
}

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

public class OAuthCallbackDto
{
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class TelegramLoginRequestDto
{
    public string InitData { get; set; } = string.Empty;
}

public class LinkAuthMethodRequestDto
{
    public string Provider { get; set; } = string.Empty;
    public string? ExternalToken { get; set; }
}

public class AuthMethodDto
{
    public string Provider { get; set; } = string.Empty;
    public DateTime LinkedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
}
