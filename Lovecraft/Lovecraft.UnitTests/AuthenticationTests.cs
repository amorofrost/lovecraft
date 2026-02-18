using Xunit;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lovecraft.UnitTests;

public class AuthenticationTests
{
    private readonly IJwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly MockAuthService _authService;

    public AuthenticationTests()
    {
        var jwtSettings = new JwtSettings
        {
            SecretKey = "test-secret-key-min-32-characters!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 7
        };

        var jwtLogger = NullLogger<JwtService>.Instance;
        var authLogger = NullLogger<MockAuthService>.Instance;

        _jwtService = new JwtService(jwtSettings, jwtLogger);
        _passwordHasher = new PasswordHasher();
        _authService = new MockAuthService(_jwtService, _passwordHasher, authLogger);
    }

    [Fact]
    public async Task Register_WithValidData_ReturnsAuthResponse()
    {
        // Arrange
        var request = new RegisterRequestDto
        {
            Email = "newuser@example.com",
            Password = "Test123!@#",
            Name = "New User",
            Age = 25,
            Location = "Test City",
            Gender = "Male",
            Bio = "Test bio"
        };

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.AccessToken);
        Assert.NotEmpty(result.RefreshToken);
        Assert.Equal(request.Email, result.User.Email);
        Assert.False(result.User.EmailVerified); // Email not verified yet
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAuthResponse()
    {
        // Arrange
        // First verify the test user's email
        var verifyResult = await _authService.VerifyEmailAsync("test-token");
        
        // For mock service, we need to manually verify test user
        // In real implementation, this would come from database
        
        var loginRequest = new LoginRequestDto
        {
            Email = "test@example.com",
            Password = "Test123!@#"
        };

        // Act
        var result = await _authService.LoginAsync(loginRequest);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.AccessToken);
        Assert.NotEmpty(result.RefreshToken);
        Assert.True(result.User.EmailVerified);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsNull()
    {
        // Arrange
        var loginRequest = new LoginRequestDto
        {
            Email = "test@example.com",
            Password = "WrongPassword123!"
        };

        // Act
        var result = await _authService.LoginAsync(loginRequest);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_ReturnsNewTokens()
    {
        // Arrange
        var loginRequest = new LoginRequestDto
        {
            Email = "test@example.com",
            Password = "Test123!@#"
        };

        var loginResult = await _authService.LoginAsync(loginRequest);
        Assert.NotNull(loginResult);

        var refreshToken = loginResult.RefreshToken;

        // Act
        var result = await _authService.RefreshTokenAsync(refreshToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.AccessToken);
        Assert.NotEmpty(result.RefreshToken);
        Assert.NotEqual(refreshToken, result.RefreshToken); // New refresh token
    }

    [Fact]
    public void PasswordHasher_HashAndVerify_WorksCorrectly()
    {
        // Arrange
        var password = "TestPassword123!";

        // Act
        var hash = _passwordHasher.HashPassword(password);
        var isValid = _passwordHasher.VerifyPassword(password, hash);
        var isInvalid = _passwordHasher.VerifyPassword("WrongPassword", hash);

        // Assert
        Assert.NotEmpty(hash);
        Assert.True(isValid);
        Assert.False(isInvalid);
    }

    [Fact]
    public void JwtService_GenerateAndValidateToken_WorksCorrectly()
    {
        // Arrange
        var userId = "test-user-id";
        var email = "test@example.com";
        var name = "Test User";

        // Act
        var token = _jwtService.GenerateAccessToken(userId, email, name);
        var principal = _jwtService.ValidateToken(token);
        var extractedUserId = _jwtService.GetUserIdFromToken(token);

        // Assert
        Assert.NotEmpty(token);
        Assert.NotNull(principal);
        Assert.Equal(userId, extractedUserId);
    }

    [Fact]
    public void JwtService_InvalidToken_ReturnsNull()
    {
        // Arrange
        var invalidToken = "invalid.token.here";

        // Act
        var principal = _jwtService.ValidateToken(invalidToken);
        var userId = _jwtService.GetUserIdFromToken(invalidToken);

        // Assert
        Assert.Null(principal);
        Assert.Null(userId);
    }

    [Fact]
    public async Task ChangePassword_WithValidCurrentPassword_ReturnsTrue()
    {
        // Arrange
        // Register a new user
        var registerRequest = new RegisterRequestDto
        {
            Email = "passwordtest@example.com",
            Password = "OldPass123!@#",
            Name = "Password Test",
            Age = 30,
            Location = "Test",
            Gender = "Male",
            Bio = "Test"
        };

        var registerResult = await _authService.RegisterAsync(registerRequest);
        Assert.NotNull(registerResult);

        var userId = registerResult.User.Id;

        // Act
        var result = await _authService.ChangePasswordAsync(userId, "OldPass123!@#", "NewPass123!@#");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ChangePassword_WithInvalidCurrentPassword_ReturnsFalse()
    {
        // Arrange
        var loginRequest = new LoginRequestDto
        {
            Email = "test@example.com",
            Password = "Test123!@#"
        };

        var loginResult = await _authService.LoginAsync(loginRequest);
        Assert.NotNull(loginResult);

        var userId = loginResult.User.Id;

        // Act
        var result = await _authService.ChangePasswordAsync(userId, "WrongPassword", "NewPass123!@#");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetAuthMethods_ReturnsUserAuthMethods()
    {
        // Arrange
        var loginRequest = new LoginRequestDto
        {
            Email = "test@example.com",
            Password = "Test123!@#"
        };

        var loginResult = await _authService.LoginAsync(loginRequest);
        Assert.NotNull(loginResult);

        var userId = loginResult.User.Id;

        // Act
        var methods = await _authService.GetAuthMethodsAsync(userId);

        // Assert
        Assert.NotNull(methods);
        Assert.NotEmpty(methods);
        Assert.Contains(methods, m => m.Provider == "local");
    }
}
