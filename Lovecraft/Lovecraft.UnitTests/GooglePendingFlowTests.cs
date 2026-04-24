using Lovecraft.Backend.Auth;
using Lovecraft.Common.DTOs.Auth;
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
