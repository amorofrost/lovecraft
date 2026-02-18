using Lovecraft.Common.DTOs.Auth;
using Lovecraft.Backend.Auth;

namespace Lovecraft.Backend.Services;

public class MockAuthService : IAuthService
{
    private readonly IJwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<MockAuthService> _logger;

    // Mock in-memory storage
    private static readonly Dictionary<string, MockUser> _users = new();
    private static readonly Dictionary<string, string> _refreshTokens = new();
    private static readonly Dictionary<string, string> _verificationTokens = new();
    private static readonly Dictionary<string, PasswordResetToken> _resetTokens = new();

    public MockAuthService(
        IJwtService jwtService,
        IPasswordHasher passwordHasher,
        ILogger<MockAuthService> logger)
    {
        _jwtService = jwtService;
        _passwordHasher = passwordHasher;
        _logger = logger;

        // Seed with test user
        SeedTestUsers();
    }

    private void SeedTestUsers()
    {
        if (_users.Count == 0)
        {
            var testUser = new MockUser
            {
                Id = "test-user-001",
                Email = "test@example.com",
                Name = "Test User",
                PasswordHash = _passwordHasher.HashPassword("Test123!@#"),
                EmailVerified = true,
                AuthMethods = new List<string> { "local" },
                CreatedAt = DateTime.UtcNow
            };
            _users[testUser.Email.ToLower()] = testUser;
        }
    }

    public async Task<AuthResponseDto?> RegisterAsync(RegisterRequestDto request)
    {
        await Task.Delay(100); // Simulate async operation

        // Validate email doesn't exist
        if (_users.ContainsKey(request.Email.ToLower()))
        {
            _logger.LogWarning("Registration failed: Email already exists {Email}", request.Email);
            return null;
        }

        // Create new user
        var userId = Guid.NewGuid().ToString();
        var user = new MockUser
        {
            Id = userId,
            Email = request.Email,
            Name = request.Name,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            EmailVerified = false, // Must verify email
            AuthMethods = new List<string> { "local" },
            CreatedAt = DateTime.UtcNow,
            Age = request.Age,
            Location = request.Location,
            Gender = request.Gender,
            Bio = request.Bio
        };

        _users[user.Email.ToLower()] = user;

        // Generate email verification token
        var verificationToken = Guid.NewGuid().ToString();
        _verificationTokens[verificationToken] = userId;

        _logger.LogInformation("User registered: {UserId}, Email: {Email}. Verification token: {Token}", 
            userId, request.Email, verificationToken);

        // In a real implementation, send email here
        // For mock, just log the token

        // Generate tokens (but user can't use them until email verified)
        var accessToken = _jwtService.GenerateAccessToken(userId, user.Email, user.Name);
        var refreshToken = _jwtService.GenerateRefreshToken();
        _refreshTokens[refreshToken] = userId;

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = userId,
                Email = user.Email,
                Name = user.Name,
                EmailVerified = false,
                AuthMethods = user.AuthMethods
            },
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto request)
    {
        await Task.Delay(100); // Simulate async operation

        var identifier = request.Email.ToLower();
        
        if (!_users.TryGetValue(identifier, out var user))
        {
            _logger.LogWarning("Login failed: User not found {Email}", request.Email);
            return null;
        }

        // Verify password
        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed: Invalid password for {Email}", request.Email);
            return null;
        }

        // Check email verification
        if (!user.EmailVerified)
        {
            _logger.LogWarning("Login failed: Email not verified for {Email}", request.Email);
            return null;
        }

        // Generate tokens
        var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Email, user.Name);
        var refreshToken = _jwtService.GenerateRefreshToken();
        _refreshTokens[refreshToken] = user.Id;

        user.LastLoginAt = DateTime.UtcNow;

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.Name,
                EmailVerified = user.EmailVerified,
                AuthMethods = user.AuthMethods
            },
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
    }

    public async Task<AuthResponseDto?> RefreshTokenAsync(string refreshToken)
    {
        await Task.Delay(50);

        if (!_refreshTokens.TryGetValue(refreshToken, out var userId))
        {
            _logger.LogWarning("Refresh token not found");
            return null;
        }

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return null;
        }

        // Revoke old refresh token
        _refreshTokens.Remove(refreshToken);

        // Generate new tokens
        var newAccessToken = _jwtService.GenerateAccessToken(user.Id, user.Email, user.Name);
        var newRefreshToken = _jwtService.GenerateRefreshToken();
        _refreshTokens[newRefreshToken] = userId;

        return new AuthResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.Name,
                EmailVerified = user.EmailVerified,
                AuthMethods = user.AuthMethods
            },
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
    }

    public async Task<bool> VerifyEmailAsync(string token)
    {
        await Task.Delay(50);

        if (!_verificationTokens.TryGetValue(token, out var userId))
        {
            _logger.LogWarning("Invalid verification token");
            return false;
        }

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return false;
        }

        user.EmailVerified = true;
        _verificationTokens.Remove(token);

        _logger.LogInformation("Email verified for user {UserId}", userId);
        return true;
    }

    public async Task<bool> ForgotPasswordAsync(string email)
    {
        await Task.Delay(100);

        if (!_users.TryGetValue(email.ToLower(), out var user))
        {
            // Don't reveal if email exists
            _logger.LogWarning("Password reset requested for non-existent email {Email}", email);
            return true;
        }

        var resetToken = Guid.NewGuid().ToString();
        _resetTokens[resetToken] = new PasswordResetToken
        {
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        _logger.LogInformation("Password reset token generated for {Email}: {Token}", email, resetToken);
        
        // In real implementation, send email
        return true;
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        await Task.Delay(100);

        if (!_resetTokens.TryGetValue(token, out var resetToken))
        {
            _logger.LogWarning("Invalid reset token");
            return false;
        }

        if (resetToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Expired reset token");
            _resetTokens.Remove(token);
            return false;
        }

        var user = _users.Values.FirstOrDefault(u => u.Id == resetToken.UserId);
        if (user == null)
        {
            return false;
        }

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        _resetTokens.Remove(token);

        // Revoke all refresh tokens for security
        await RevokeAllUserTokensAsync(user.Id);

        _logger.LogInformation("Password reset successful for user {UserId}", user.Id);
        return true;
    }

    public async Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        await Task.Delay(100);

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return false;
        }

        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            _logger.LogWarning("Change password failed: Invalid current password for {UserId}", userId);
            return false;
        }

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        
        // Revoke all refresh tokens
        await RevokeAllUserTokensAsync(userId);

        _logger.LogInformation("Password changed for user {UserId}", userId);
        return true;
    }

    public async Task<UserInfo?> GetCurrentUserAsync(string userId)
    {
        await Task.Delay(50);

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return null;
        }

        return new UserInfo
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            EmailVerified = user.EmailVerified,
            AuthMethods = user.AuthMethods
        };
    }

    public async Task<List<AuthMethodDto>> GetAuthMethodsAsync(string userId)
    {
        await Task.Delay(50);

        var user = _users.Values.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return new List<AuthMethodDto>();
        }

        return user.AuthMethods.Select(method => new AuthMethodDto
        {
            Provider = method,
            LinkedAt = user.CreatedAt,
            LastUsedAt = user.LastLoginAt ?? user.CreatedAt
        }).ToList();
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        await Task.Delay(50);
        _refreshTokens.Remove(refreshToken);
    }

    public async Task RevokeAllUserTokensAsync(string userId)
    {
        await Task.Delay(50);
        
        var tokensToRemove = _refreshTokens.Where(kvp => kvp.Value == userId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var token in tokensToRemove)
        {
            _refreshTokens.Remove(token);
        }

        _logger.LogInformation("Revoked all tokens for user {UserId}", userId);
    }

    private class MockUser
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public bool EmailVerified { get; set; }
        public List<string> AuthMethods { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public int Age { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
    }

    private class PasswordResetToken
    {
        public string UserId { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}
