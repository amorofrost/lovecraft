namespace Lovecraft.Backend.Auth;

public class JwtSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "AloeVeraAPI";
    public string Audience { get; set; } = "AloeVeraClients";
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 7;
}

public class JwtToken
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
