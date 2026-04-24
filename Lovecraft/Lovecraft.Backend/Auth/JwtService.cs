using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Lovecraft.Common.DTOs.Auth;
using Microsoft.IdentityModel.Tokens;

namespace Lovecraft.Backend.Auth;

public interface IJwtService
{
    string GenerateAccessToken(string userId, string email, string name, string staffRole = "none");
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateToken(string token);
    string? GetUserIdFromToken(string token);

    /// <summary>
    /// Short-lived JWT issued when a Telegram Login Widget payload is verified for an unknown
    /// Telegram user. The ticket carries the verified Telegram identity and must be redeemed via
    /// <c>/telegram-register</c>, <c>/telegram-link-login</c>, or <c>/telegram-link</c> before it
    /// expires. Stateless (no server-side single-use enforcement) — 15-min TTL is the only bound.
    /// </summary>
    string GenerateTelegramPendingTicket(TelegramUserInfoDto telegram);

    /// <summary>Validate a pending-Telegram ticket and return the encoded Telegram identity, or null.</summary>
    TelegramUserInfoDto? ValidateTelegramPendingTicket(string ticket);

    /// <summary>Short-lived ticket after Google ID token verification for a user who still needs to complete profile/invite at <c>/welcome/google</c>.</summary>
    string GenerateGooglePendingTicket(GoogleUserInfoDto google);

    /// <summary>Validate a pending-Google ticket and return the encoded Google identity, or null.</summary>
    GoogleUserInfoDto? ValidateGooglePendingTicket(string ticket);
}

public class JwtService : IJwtService
{
    private readonly JwtSettings _settings;
    private readonly ILogger<JwtService> _logger;

    public JwtService(JwtSettings settings, ILogger<JwtService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public string GenerateAccessToken(string userId, string email, string name, string staffRole = "none")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_settings.SecretKey);
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, "user"),
            new Claim("staffRole", staffRole ?? "none"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_settings.AccessTokenLifetimeMinutes),
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_settings.SecretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = true,
                ValidAudience = _settings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    public string? GetUserIdFromToken(string token)
    {
        var principal = ValidateToken(token);
        return principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    private const string TelegramPendingAudience = "AloeVera.TelegramPending";
    private static readonly TimeSpan TelegramPendingLifetime = TimeSpan.FromMinutes(15);

    private const string GooglePendingAudience = "AloeVera.GooglePending";
    private static readonly TimeSpan GooglePendingLifetime = TimeSpan.FromMinutes(15);

    public string GenerateTelegramPendingTicket(TelegramUserInfoDto telegram)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_settings.SecretKey);

        var claims = new List<Claim>
        {
            new Claim("tg_id", telegram.Id.ToString()),
            new Claim("tg_first_name", telegram.FirstName ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };
        if (!string.IsNullOrEmpty(telegram.LastName)) claims.Add(new Claim("tg_last_name", telegram.LastName));
        if (!string.IsNullOrEmpty(telegram.Username)) claims.Add(new Claim("tg_username", telegram.Username));
        if (!string.IsNullOrEmpty(telegram.PhotoUrl)) claims.Add(new Claim("tg_photo_url", telegram.PhotoUrl));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(TelegramPendingLifetime),
            Issuer = _settings.Issuer,
            Audience = TelegramPendingAudience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        return tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor));
    }

    public TelegramUserInfoDto? ValidateTelegramPendingTicket(string ticket)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_settings.SecretKey);

            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = true,
                ValidAudience = TelegramPendingAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = handler.ValidateToken(ticket, parameters, out _);
            var idClaim = principal.FindFirst("tg_id")?.Value;
            if (string.IsNullOrEmpty(idClaim) || !long.TryParse(idClaim, out var id))
                return null;

            return new TelegramUserInfoDto
            {
                Id = id,
                FirstName = principal.FindFirst("tg_first_name")?.Value ?? string.Empty,
                LastName = principal.FindFirst("tg_last_name")?.Value,
                Username = principal.FindFirst("tg_username")?.Value,
                PhotoUrl = principal.FindFirst("tg_photo_url")?.Value
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram pending ticket validation failed");
            return null;
        }
    }

    public string GenerateGooglePendingTicket(GoogleUserInfoDto google)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_settings.SecretKey);
        var email = google.Email.Trim().ToLowerInvariant();

        var claims = new List<Claim>
        {
            new Claim("g_sub", google.Sub),
            new Claim("g_email", email),
            new Claim("g_name", google.Name ?? string.Empty),
            new Claim("g_email_verified", google.EmailVerified ? "true" : "false"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };
        if (!string.IsNullOrEmpty(google.PictureUrl)) claims.Add(new Claim("g_picture", google.PictureUrl));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(GooglePendingLifetime),
            Issuer = _settings.Issuer,
            Audience = GooglePendingAudience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        return tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor));
    }

    public GoogleUserInfoDto? ValidateGooglePendingTicket(string ticket)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_settings.SecretKey);
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = true,
                ValidAudience = GooglePendingAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
            var principal = handler.ValidateToken(ticket, parameters, out _);
            var sub = principal.FindFirst("g_sub")?.Value;
            var email = principal.FindFirst("g_email")?.Value;
            if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email))
                return null;

            return new GoogleUserInfoDto
            {
                Sub = sub,
                Email = email,
                EmailVerified = string.Equals(
                    principal.FindFirst("g_email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                Name = principal.FindFirst("g_name")?.Value ?? string.Empty,
                PictureUrl = principal.FindFirst("g_picture")?.Value
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google pending ticket validation failed");
            return null;
        }
    }
}
