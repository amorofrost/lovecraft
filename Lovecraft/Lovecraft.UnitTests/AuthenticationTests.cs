using System.Net.Http.Json;
using Xunit;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Configuration;
using Lovecraft.Common.DTOs.Auth;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        var (app, invites, events) = TestAuthDependencies.CreateMockStack();
        _authService = new MockAuthService(
            _jwtService,
            _passwordHasher,
            authLogger,
            new NullEmailService(NullLogger<NullEmailService>.Instance),
            app,
            invites,
            events,
            Options.Create(new TelegramAuthOptions()),
            Options.Create(new GoogleAuthOptions()));
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

    [Fact]
    public async Task Register_WithOptionalInviteField_Succeeds()
    {
        var request = new RegisterRequestDto
        {
            Email = "open-reg@example.com", Password = "Test123!@#",
            Name = "Open User", Age = 25, Location = "City", Gender = "Male", Bio = "",
            InviteCode = null
        };

        var result = await _authService.RegisterAsync(request);

        Assert.NotNull(result);
        Assert.Equal(request.Email, result.User.Email);
    }

    [Fact]
    public async Task Login_EmbedsStaffRoleInToken()
    {
        const string userId = "test-user-001";
        var original = MockDataStore.UserStaffRoles.TryGetValue(userId, out var prior) ? (StaffRole?)prior : null;
        MockDataStore.UserStaffRoles[userId] = StaffRole.Moderator;
        try
        {
            var result = await _authService.LoginAsync(new LoginRequestDto
            {
                Email = "test@example.com",
                Password = "Test123!@#"
            });

            Assert.NotNull(result);
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(result!.AccessToken);
            Assert.Equal("moderator", token.Claims.First(c => c.Type == "staffRole").Value);
        }
        finally
        {
            if (original.HasValue) MockDataStore.UserStaffRoles[userId] = original.Value;
            else MockDataStore.UserStaffRoles.Remove(userId);
        }
    }
}

/// <summary>
/// Integration tests for GET /api/v1/auth/account-name-availability.
/// These will return 404 until Task 4 adds the controller route.
/// </summary>
[Collection("AccountNameAvailabilityTests")]
public class AccountNameAvailabilityTests : IClassFixture<AclTests.TestAppFactory>
{
    private readonly AclTests.TestAppFactory _factory;

    public AccountNameAvailabilityTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CheckAccountName_AvailableForFreshName()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/auth/account-name-availability?name=fresh_user_001");
        var dto = await resp.Content.ReadFromJsonAsync<ApiResponse<AccountNameAvailabilityDto>>();
        Assert.True(dto!.Success);
        Assert.True(dto.Data!.Available);
        Assert.Null(dto.Data.Reason);
    }

    [Fact]
    public async Task CheckAccountName_RejectsInvalidFormat()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/auth/account-name-availability?name=ab");
        var dto = await resp.Content.ReadFromJsonAsync<ApiResponse<AccountNameAvailabilityDto>>();
        Assert.True(dto!.Success);
        Assert.False(dto.Data!.Available);
        Assert.Equal("invalidFormat", dto.Data.Reason);
    }

    [Fact]
    public async Task CheckAccountName_RejectsReserved()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/auth/account-name-availability?name=admin");
        var dto = await resp.Content.ReadFromJsonAsync<ApiResponse<AccountNameAvailabilityDto>>();
        Assert.True(dto!.Success);
        Assert.False(dto.Data!.Available);
        Assert.Equal("reserved", dto.Data.Reason);
    }
}
