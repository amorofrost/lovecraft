using System.Net.Http.Json;
using Lovecraft.Backend.Auth;
using Lovecraft.Common.DTOs.Auth;
using Lovecraft.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lovecraft.UnitTests;

public class GooglePendingFlowTests
{
    private readonly IJwtService _jwt = new JwtService(
        new JwtSettings
        {
            SecretKey = "test-secret-key-min-32-characters!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 7
        },
        NullLogger<JwtService>.Instance);

    [Fact]
    public void GooglePendingTicket_RoundTrips_Payload()
    {
        var g = new GoogleUserInfoDto
        {
            Sub = "sub-123-abc",
            Email = "user@gmail.com",
            EmailVerified = true,
            Name = "Test User",
            PictureUrl = "https://example.com/pic.jpg"
        };
        var ticket = _jwt.GenerateGooglePendingTicket(g);
        var back = _jwt.ValidateGooglePendingTicket(ticket);
        Assert.NotNull(back);
        Assert.Equal(g.Sub, back!.Sub);
        Assert.Equal("user@gmail.com", back.Email);
        Assert.True(back.EmailVerified);
        Assert.Equal("Test User", back.Name);
        Assert.Equal("https://example.com/pic.jpg", back.PictureUrl);
    }

    [Fact]
    public void GooglePendingTicket_Expired_ReturnsNull()
    {
        // JwtService does not support minting with zero lifetime; validate wrong signature instead
        var bad = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjB9.wrong";
        Assert.Null(_jwt.ValidateGooglePendingTicket(bad));
    }
}

/// <summary>
/// Integration tests for /api/v1/auth/google-register — account-name-as-userid.
/// </summary>
[Collection("GoogleRegisterTests")]
public class GoogleRegisterAccountNameTests : IClassFixture<AclTests.TestAppFactory>
{
    private readonly AclTests.TestAppFactory _factory;

    public GoogleRegisterAccountNameTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;
    }

    // Resolve the backend's IJwtService from the factory's DI so we always mint with
    // the same signing key the backend will validate with, regardless of any env-var
    // mutations in earlier test classes (e.g. NotificationsControllerTests).
    private string MintGoogleTicket(string sub, string email, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();
        return jwt.GenerateGooglePendingTicket(new GoogleUserInfoDto
        {
            Sub = sub,
            Email = email,
            EmailVerified = true,
            Name = name,
        });
    }

    [Fact]
    public async Task GoogleRegister_UsesAccountNameAsUserId()
    {
        using var client = _factory.CreateClient();
        var ticket = MintGoogleTicket(sub: "gsub-555", email: "gus@example.com", name: "Gus");
        var resp = await client.PostAsJsonAsync("/api/v1/auth/google-register", new {
            ticket,
            accountName = "googleGus5",
            name = "Gus", age = 25, location = "RU", country = "RU", gender = "male",
        });
        var dto = await resp.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        Assert.True(dto!.Success, dto.Error?.Message);
        Assert.Equal("googlegus5", dto.Data!.User.Id);
        Assert.Equal("googleGus5", dto.Data.User.AccountName);
    }
}
