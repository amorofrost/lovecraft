using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Configuration;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lovecraft.UnitTests;

[Collection("AuthTests")]
public class PreRegistrationTests
{
    private const string BotToken = "1234567:TEST-BOT-TOKEN-FOR-PREREGISTRATION";
    private const string EventId = "1";

    private readonly MockAuthService _auth;

    public PreRegistrationTests()
    {
        var jwtSettings = new JwtSettings
        {
            SecretKey = "test-secret-key-min-32-characters!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 7
        };
        var jwt = new JwtService(jwtSettings, NullLogger<JwtService>.Instance);
        var (app, invites, events) = TestAuthDependencies.CreateMockStack();
        _auth = new MockAuthService(
            jwt,
            new PasswordHasher(),
            NullLogger<MockAuthService>.Instance,
            new NullEmailService(NullLogger<NullEmailService>.Instance),
            app,
            invites,
            events,
            Options.Create(new TelegramAuthOptions { BotToken = BotToken, BotUsername = "testbot" }),
            Options.Create(new GoogleAuthOptions()));
    }

    private static PreRegisterAttendeeDto Row(string username, string name = "Test Person") =>
        new() { TelegramUsername = username, Name = name, Gender = "female" };

    [Fact]
    public async Task PreRegister_CreatesShellAccount_WithNormalizedUsernameAsUserId()
    {
        var result = await _auth.PreRegisterAttendeesAsync(EventId, new() { Row("Anna_Petrova") });

        Assert.Equal(1, result.Summary.Created);
        var row = Assert.Single(result.Results);
        Assert.Equal("created", row.Status);
        Assert.Equal("anna_petrova", row.UserId);
    }

    [Fact]
    public async Task PreRegister_IsIdempotent_SecondImportSkips()
    {
        await _auth.PreRegisterAttendeesAsync(EventId, new() { Row("Repeat_User") });
        var second = await _auth.PreRegisterAttendeesAsync(EventId, new() { Row("Repeat_User") });

        Assert.Equal(0, second.Summary.Created);
        Assert.Equal(1, second.Summary.SkippedExists);
        Assert.Equal("skippedExists", second.Results[0].Status);
    }

    [Fact]
    public async Task PreRegister_DuplicateWithinBatch_CreatesOnce()
    {
        var result = await _auth.PreRegisterAttendeesAsync(
            EventId, new() { Row("Dup_User"), Row("dup_user") });

        Assert.Equal(1, result.Summary.Created);
        Assert.Equal(1, result.Summary.SkippedExists);
    }

    [Theory]
    [InlineData("abc")]        // too short (min 5)
    [InlineData("1nvalid")]    // must start with a letter
    [InlineData("bad-name")]   // hyphen not allowed
    [InlineData("official")]   // reserved
    public async Task PreRegister_InvalidUsername_IsReportedNotCreated(string username)
    {
        var result = await _auth.PreRegisterAttendeesAsync(EventId, new() { Row(username) });

        Assert.Equal(0, result.Summary.Created);
        Assert.Equal(1, result.Summary.InvalidUsername);
        Assert.Equal("invalidUsername", result.Results[0].Status);
    }

    [Fact]
    public async Task PreRegister_NameContainingHtml_IsRejected()
    {
        var result = await _auth.PreRegisterAttendeesAsync(
            EventId, new() { Row("Html_User", "<b>bold</b>") });

        Assert.Equal(0, result.Summary.Created);
        Assert.Equal(1, result.Summary.InvalidName);
        Assert.Equal("invalidName", result.Results[0].Status);
    }

    [Fact]
    public async Task PreRegister_ShellIsNotLoggableUntilClaimed_HasNoAuthMethods()
    {
        await _auth.PreRegisterAttendeesAsync(EventId, new() { Row("Shell_User") });

        // IAuthService.GetAuthMethodsAsync returns List<AuthMethodDto> (Provider/LinkedAt/LastUsedAt).
        var methods = await _auth.GetAuthMethodsAsync("shell_user");
        Assert.Empty(methods);
    }

    [Fact]
    public async Task PreRegister_MixedBatch_ReportsEachRowIndependently()
    {
        var result = await _auth.PreRegisterAttendeesAsync(
            EventId, new() { Row("Good_User"), Row("bad"), Row("Other_User") });

        Assert.Equal(2, result.Summary.Created);
        Assert.Equal(1, result.Summary.InvalidUsername);
        Assert.Equal(3, result.Results.Count);
    }
}
