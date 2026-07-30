using Xunit;
using Lovecraft.Backend.Configuration;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lovecraft.UnitTests;

public class EmailServiceTests
{
    [Fact]
    public async Task NullEmailService_SendVerification_CompletesWithoutThrowing()
    {
        var svc = new NullEmailService(NullLogger<NullEmailService>.Instance);
        await svc.SendVerificationEmailAsync("user@example.com", "Alice", "token-123");
    }

    [Fact]
    public async Task NullEmailService_SendPasswordReset_CompletesWithoutThrowing()
    {
        var svc = new NullEmailService(NullLogger<NullEmailService>.Instance);
        await svc.SendPasswordResetEmailAsync("user@example.com", "Alice", "token-456");
    }

    [Fact]
    public void AzureEmailService_ThrowsWhenConnectionStringMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new AzureEmailService(config, NullLogger<AzureEmailService>.Instance));
    }

    [Fact]
    public void AzureEmailService_ConstructsSuccessfullyWithConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AZURE_COMMUNICATION_CONNECTION_STRING"] = "endpoint=https://test.communication.azure.com/;accesskey=dGVzdA==",
                ["FROM_EMAIL"] = "test@example.com",
                ["FRONTEND_BASE_URL"] = "https://example.com"
            })
            .Build();

        var svc = new AzureEmailService(config, NullLogger<AzureEmailService>.Instance);
        Assert.NotNull(svc);
    }
}

// Spy used to verify MockAuthService calls IEmailService correctly.
internal class CapturingEmailService : IEmailService
{
    public List<(string email, string name, string token)> VerificationsSent = new();
    public List<(string email, string name, string token)> ResetsSent = new();
    public bool ShouldThrow { get; set; }

    public Task SendVerificationEmailAsync(string email, string name, string token)
    {
        if (ShouldThrow) throw new InvalidOperationException("SendGrid unavailable");
        VerificationsSent.Add((email, name, token));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string email, string name, string token)
    {
        if (ShouldThrow) throw new InvalidOperationException("SendGrid unavailable");
        ResetsSent.Add((email, name, token));
        return Task.CompletedTask;
    }
}

// MockAuthService uses static dictionaries — must be in [Collection("AuthTests")]
// to be serialised with AuthenticationTests and RefreshTokenTests.
[Collection("AuthTests")]
public class MockAuthServiceEmailTests
{
    private static MockAuthService BuildAuthSvc(IEmailService emailSvc)
    {
        var jwtSettings = new JwtSettings
        {
            SecretKey = "test-secret-key-min-32-characters!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 7
        };
        var (app, invites, events) = TestAuthDependencies.CreateMockStack();
        return new MockAuthService(
            new JwtService(jwtSettings, NullLogger<JwtService>.Instance),
            new PasswordHasher(),
            NullLogger<MockAuthService>.Instance,
            emailSvc,
            app,
            invites,
            events,
            Options.Create(new TelegramAuthOptions()),
            Options.Create(new GoogleAuthOptions()));
    }

    [Fact]
    public async Task Register_SendsVerificationEmail()
    {
        var emailSvc = new CapturingEmailService();
        var authSvc = BuildAuthSvc(emailSvc);

        await authSvc.RegisterAsync(new Lovecraft.Common.DTOs.Auth.RegisterRequestDto
        {
            Email = "alice@example.com",
            AccountName = "alice25",
            Password = "Password1!",
            Name = "Alice",
            Age = 25,
            Location = "Moscow",
            Gender = "female"
        });

        Assert.Single(emailSvc.VerificationsSent);
        Assert.Equal("alice@example.com", emailSvc.VerificationsSent[0].email);
        Assert.Equal("Alice", emailSvc.VerificationsSent[0].name);
    }

    [Fact]
    public async Task ForgotPassword_ExistingUser_SendsResetEmail()
    {
        var emailSvc = new CapturingEmailService();
        var authSvc = BuildAuthSvc(emailSvc);

        await authSvc.RegisterAsync(new Lovecraft.Common.DTOs.Auth.RegisterRequestDto
        {
            Email = "bob@example.com", AccountName = "bob30", Password = "Password1!", Name = "Bob",
            Age = 30, Location = "Berlin", Gender = "male"
        });
        emailSvc.VerificationsSent.Clear();

        var result = await authSvc.ForgotPasswordAsync("bob@example.com");

        Assert.True(result);
        Assert.Single(emailSvc.ResetsSent);
        Assert.Equal("bob@example.com", emailSvc.ResetsSent[0].email);
        Assert.Equal("Bob", emailSvc.ResetsSent[0].name);
    }

    [Fact]
    public async Task ForgotPassword_NonExistentUser_ReturnsTrueAndSendsNoEmail()
    {
        var emailSvc = new CapturingEmailService();
        var authSvc = BuildAuthSvc(emailSvc);

        var result = await authSvc.ForgotPasswordAsync("nobody@example.com");

        Assert.True(result);
        Assert.Empty(emailSvc.ResetsSent);
    }

    [Fact]
    public async Task Register_EmailServiceThrows_DoesNotRethrow()
    {
        var emailSvc = new CapturingEmailService { ShouldThrow = true };
        var authSvc = BuildAuthSvc(emailSvc);

        var result = await authSvc.RegisterAsync(new Lovecraft.Common.DTOs.Auth.RegisterRequestDto
        {
            Email = "carol@example.com", AccountName = "carol22", Password = "Password1!", Name = "Carol",
            Age = 22, Location = "Paris", Gender = "female"
        });

        Assert.NotNull(result);
    }
}
