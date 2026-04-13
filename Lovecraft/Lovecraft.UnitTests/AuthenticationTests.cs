using Xunit;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lovecraft.UnitTests;

// MockAuthService uses static dictionaries shared across instances.
// The [Collection] attribute serialises this class with RefreshTokenTests
// so the two classes never race on that shared state.
[Collection("AuthTests")]
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
        _authService = new MockAuthService(_jwtService, _passwordHasher, authLogger, new NullEmailService(NullLogger<NullEmailService>.Instance), new ConfigurationBuilder().Build());
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

    // --- Invite code tests ---

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration WithInviteCode(string code) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["INVITE_CODE"] = code })
            .Build();

    private MockAuthService BuildServiceWith(IConfiguration config) =>
        new MockAuthService(
            _jwtService,
            _passwordHasher,
            NullLogger<MockAuthService>.Instance,
            new NullEmailService(NullLogger<NullEmailService>.Instance),
            config);

    [Fact]
    public async Task Register_WithNoInviteCodeConfigured_SucceedsRegardlessOfSubmittedCode()
    {
        var service = BuildServiceWith(EmptyConfig());
        var request = new RegisterRequestDto
        {
            Email = "open-reg@example.com", Password = "Test123!@#",
            Name = "Open User", Age = 25, Location = "City", Gender = "Male", Bio = "",
            InviteCode = "anything"
        };

        var result = await service.RegisterAsync(request);

        Assert.NotNull(result);
        Assert.Equal(request.Email, result.User.Email);
    }

    [Fact]
    public async Task Register_WithValidInviteCode_Succeeds()
    {
        var service = BuildServiceWith(WithInviteCode("SECRET123"));
        var request = new RegisterRequestDto
        {
            Email = "valid-invite@example.com", Password = "Test123!@#",
            Name = "Invited User", Age = 25, Location = "City", Gender = "Male", Bio = "",
            InviteCode = "SECRET123"
        };

        var result = await service.RegisterAsync(request);

        Assert.NotNull(result);
        Assert.Equal(request.Email, result.User.Email);
    }

    [Fact]
    public async Task Register_WithWrongInviteCode_ThrowsInvalidInviteCodeException()
    {
        var service = BuildServiceWith(WithInviteCode("SECRET123"));
        var request = new RegisterRequestDto
        {
            Email = "wrong-invite@example.com", Password = "Test123!@#",
            Name = "Bad User", Age = 25, Location = "City", Gender = "Male", Bio = "",
            InviteCode = "WRONGCODE"
        };

        await Assert.ThrowsAsync<InvalidInviteCodeException>(() => service.RegisterAsync(request));
    }

    [Fact]
    public async Task Register_WithNullInviteCodeWhenCodeConfigured_ThrowsInvalidInviteCodeException()
    {
        var service = BuildServiceWith(WithInviteCode("SECRET123"));
        var request = new RegisterRequestDto
        {
            Email = "null-invite@example.com", Password = "Test123!@#",
            Name = "Null Code User", Age = 25, Location = "City", Gender = "Male", Bio = "",
            InviteCode = null
        };

        await Assert.ThrowsAsync<InvalidInviteCodeException>(() => service.RegisterAsync(request));
    }
}
