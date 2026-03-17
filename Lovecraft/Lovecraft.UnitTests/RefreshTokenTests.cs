using System.IdentityModel.Tokens.Jwt;
using Xunit;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lovecraft.UnitTests;

// MockAuthService uses static dictionaries shared across all instances in a
// test run. The [Collection] attribute serialises this class with
// AuthenticationTests so the two never race on that shared state.
[Collection("AuthTests")]
public class RefreshTokenTests
{
    private readonly IJwtService _jwtService;
    private readonly MockAuthService _authService;

    private const string TestEmail    = "test@example.com";
    private const string TestPassword = "Test123!@#";

    public RefreshTokenTests()
    {
        var jwtSettings = new JwtSettings
        {
            SecretKey                  = "test-secret-key-min-32-characters!",
            Issuer                     = "TestIssuer",
            Audience                   = "TestAudience",
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays   = 7
        };

        _jwtService  = new JwtService(jwtSettings, NullLogger<JwtService>.Instance);
        _authService = new MockAuthService(_jwtService, new PasswordHasher(),
                                           NullLogger<MockAuthService>.Instance,
                                           new NullEmailService(NullLogger<NullEmailService>.Instance));
    }

    /// <summary>
    /// Login as the pre-seeded test user and return a fresh AuthResponseDto.
    /// The test user has EmailVerified = true so LoginAsync always succeeds.
    /// </summary>
    private async Task<AuthResponseDto> LoginTestUser()
    {
        var result = await _authService.LoginAsync(
            new LoginRequestDto { Email = TestEmail, Password = TestPassword });
        Assert.NotNull(result); // Guard: login must succeed for tests to be valid
        return result;
    }

    // ─── Happy-path: obtaining new tokens ────────────────────────────────────

    [Fact]
    public async Task RefreshToken_WithValidToken_IssuesNewAccessToken()
    {
        var login = await LoginTestUser();

        var result = await _authService.RefreshTokenAsync(login.RefreshToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result.AccessToken);
        // Must be a syntactically valid JWT (three base64 segments)
        Assert.Equal(3, result.AccessToken.Split('.').Length);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_IssuesNewRefreshToken()
    {
        var login = await LoginTestUser();

        var result = await _authService.RefreshTokenAsync(login.RefreshToken);

        Assert.NotNull(result);
        Assert.NotEmpty(result.RefreshToken);
        // Refresh token rotation — the new token must differ from the old one
        Assert.NotEqual(login.RefreshToken, result.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_PreservesUserIdentity()
    {
        var login  = await LoginTestUser();
        var before = login.User;

        var result = await _authService.RefreshTokenAsync(login.RefreshToken);

        Assert.NotNull(result);
        Assert.Equal(before.Id,    result.User.Id);
        Assert.Equal(before.Email, result.User.Email);
        Assert.Equal(before.Name,  result.User.Name);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_AccessTokenContainsCorrectUserId()
    {
        var login = await LoginTestUser();

        var result = await _authService.RefreshTokenAsync(login.RefreshToken);

        Assert.NotNull(result);
        var userId = _jwtService.GetUserIdFromToken(result.AccessToken);
        Assert.Equal(login.User.Id, userId);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_AccessTokenPassesSignatureValidation()
    {
        var login = await LoginTestUser();

        var result = await _authService.RefreshTokenAsync(login.RefreshToken);

        Assert.NotNull(result);
        var principal = _jwtService.ValidateToken(result.AccessToken);
        Assert.NotNull(principal);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_ExpiresAtIsInFuture()
    {
        var login = await LoginTestUser();

        var result = await _authService.RefreshTokenAsync(login.RefreshToken);

        Assert.NotNull(result);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    // ─── Token rotation / replay-attack prevention ───────────────────────────

    [Fact]
    public async Task RefreshToken_TokenRotation_OldTokenIsInvalidatedAfterUse()
    {
        var login        = await LoginTestUser();
        var originalToken = login.RefreshToken;

        // First use — succeeds and rotates the token
        var first = await _authService.RefreshTokenAsync(originalToken);
        Assert.NotNull(first);

        // Second use of the same (now revoked) token — must be rejected
        var second = await _authService.RefreshTokenAsync(originalToken);
        Assert.Null(second);
    }

    [Fact]
    public async Task RefreshToken_ChainedRefreshes_EachSucceeds()
    {
        var current = await LoginTestUser();

        for (int i = 0; i < 3; i++)
        {
            var next = await _authService.RefreshTokenAsync(current.RefreshToken);
            Assert.NotNull(next);
            current = next;
        }

        // After three rotations the final token should still be usable
        Assert.NotEmpty(current.AccessToken);
        Assert.NotEmpty(current.RefreshToken);
    }

    // ─── Invalid / unknown token ─────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_WithUnknownToken_ReturnsNull()
    {
        var result = await _authService.RefreshTokenAsync("totally-unknown-token");
        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshToken_WithEmptyString_ReturnsNull()
    {
        var result = await _authService.RefreshTokenAsync(string.Empty);
        Assert.Null(result);
    }

    // ─── Explicit revocation (logout) ────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_AfterExplicitRevocation_ReturnsNull()
    {
        var login = await LoginTestUser();

        // Simulate logout — revoke the specific refresh token
        await _authService.RevokeRefreshTokenAsync(login.RefreshToken);

        var result = await _authService.RefreshTokenAsync(login.RefreshToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshToken_AfterRevokeAllUserTokens_AllTokensForUserAreInvalidated()
    {
        // Obtain two independent refresh tokens for the same user via two logins
        var loginA = await LoginTestUser();
        var loginB = await LoginTestUser();

        // Revoke every token belonging to this user
        await _authService.RevokeAllUserTokensAsync(loginA.User.Id);

        Assert.Null(await _authService.RefreshTokenAsync(loginA.RefreshToken));
        Assert.Null(await _authService.RefreshTokenAsync(loginB.RefreshToken));
    }

    [Fact]
    public async Task RevokeAllUserTokens_DoesNotAffectOtherUsers()
    {
        // Register a second, independent user.  RegisterAsync stores a refresh
        // token even without email verification, so we can test it directly.
        var otherUser = await _authService.RegisterAsync(new RegisterRequestDto
        {
            Email    = $"isolation-test-{Guid.NewGuid():N}@example.com",
            Password = "OtherPass9!",
            Name     = "Isolation User",
            Age      = 28,
            Location = "Test",
            Gender   = "Other",
            Bio      = ""
        });
        Assert.NotNull(otherUser);

        // Log in the main test user and revoke all their tokens
        var testUserLogin = await LoginTestUser();
        await _authService.RevokeAllUserTokensAsync(testUserLogin.User.Id);

        // The other user's token (held from registration) must still work
        var otherResult = await _authService.RefreshTokenAsync(otherUser.RefreshToken);
        Assert.NotNull(otherResult);
    }

    // ─── JwtService — refresh token generation ───────────────────────────────

    [Fact]
    public void GenerateRefreshToken_EachCallProducesUniqueToken()
    {
        var tokens = Enumerable.Range(0, 5)
                                .Select(_ => _jwtService.GenerateRefreshToken())
                                .ToList();

        // All tokens are non-empty
        Assert.All(tokens, t => Assert.NotEmpty(t));

        // All tokens are distinct
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void GenerateRefreshToken_ProducesBase64EncodedValue()
    {
        var token = _jwtService.GenerateRefreshToken();

        // A valid base64 string will decode without throwing
        var ex = Record.Exception(() => Convert.FromBase64String(token));
        Assert.Null(ex);
    }

    [Fact]
    public void GenerateRefreshToken_HasSufficientEntropy()
    {
        // 64 random bytes → 86 base64 chars (no padding needed for most lengths)
        var token = _jwtService.GenerateRefreshToken();
        Assert.True(token.Length >= 80,
            $"Refresh token too short ({token.Length} chars); expected ≥ 80 for 64-byte entropy");
    }
}
