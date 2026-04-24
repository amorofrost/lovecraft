using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Configuration;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lovecraft.UnitTests;

[Collection("AuthTests")]
public class TelegramMiniAppFlowTests
{
    private const string BotToken = "1234567:TEST-BOT-TOKEN-FOR-MINI-APP-VALIDATOR";

    private readonly IJwtService _jwt;
    private readonly MockAuthService _authService;

    public TelegramMiniAppFlowTests()
    {
        var jwtSettings = new JwtSettings
        {
            SecretKey = "test-secret-key-min-32-characters!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 7
        };
        _jwt = new JwtService(jwtSettings, NullLogger<JwtService>.Instance);
        var (app, invites, events) = TestAuthDependencies.CreateMockStack();
        _authService = new MockAuthService(
            _jwt,
            new PasswordHasher(),
            NullLogger<MockAuthService>.Instance,
            new NullEmailService(NullLogger<NullEmailService>.Instance),
            app,
            invites,
            events,
            Options.Create(new TelegramAuthOptions { BotToken = BotToken, BotUsername = "testbot" }),
            Options.Create(new GoogleAuthOptions()));
    }

    private static TelegramUserInfoDto NewTgInfo(long id) => new()
    {
        Id = id,
        FirstName = "Mini",
        LastName = "App",
        Username = $"mini{id}",
    };

    private static string Sign(TelegramUserInfoDto tg, long? authDate = null) =>
        TelegramInitDataValidator.BuildSigned(
            BotToken,
            tg,
            authDate ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    [Fact]
    public void Validator_ValidInitData_Returns_User()
    {
        var tg = NewTgInfo(900001);
        var initData = Sign(tg);
        var parsed = TelegramInitDataValidator.Validate(BotToken, initData);
        Assert.NotNull(parsed);
        Assert.Equal(tg.Id, parsed!.Id);
        Assert.Equal(tg.FirstName, parsed.FirstName);
        Assert.Equal(tg.Username, parsed.Username);
    }

    [Fact]
    public void Validator_TamperedHash_ReturnsNull()
    {
        var tg = NewTgInfo(900002);
        var initData = Sign(tg);
        // Flip a single hex digit of the hash.
        var hashIdx = initData.LastIndexOf("&hash=", StringComparison.Ordinal);
        Assert.True(hashIdx > 0);
        var flipped = initData.Substring(0, hashIdx + 6) + new string('0', 64);
        Assert.Null(TelegramInitDataValidator.Validate(BotToken, flipped));
    }

    [Fact]
    public void Validator_StaleAuthDate_ReturnsNull()
    {
        var tg = NewTgInfo(900003);
        var stale = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds();
        var initData = Sign(tg, stale);
        Assert.Null(TelegramInitDataValidator.Validate(BotToken, initData));
    }

    [Fact]
    public void Validator_WrongBotToken_ReturnsNull()
    {
        var tg = NewTgInfo(900004);
        var initData = Sign(tg);
        Assert.Null(TelegramInitDataValidator.Validate("different-token", initData));
    }

    [Fact]
    public async Task MiniAppLogin_UnknownTgId_ReturnsNeedsRegistration()
    {
        var tg = NewTgInfo(910001);
        var res = await _authService.MiniAppLoginAsync(new TelegramMiniAppLoginRequestDto { InitData = Sign(tg) });
        Assert.NotNull(res);
        Assert.Equal("needsRegistration", res!.Status);
        Assert.Null(res.Auth);
        Assert.NotNull(res.Telegram);
        Assert.Equal(tg.Id, res.Telegram!.Id);
    }

    [Fact]
    public async Task MiniAppLogin_InvalidInitData_ReturnsNull()
    {
        var res = await _authService.MiniAppLoginAsync(new TelegramMiniAppLoginRequestDto
        {
            InitData = "auth_date=1&user=%7B%22id%22%3A1%7D&hash=" + new string('0', 64),
        });
        Assert.Null(res);
    }

    [Fact]
    public async Task MiniAppRegister_ValidInitData_CreatesAccountAndSignsIn()
    {
        var tg = NewTgInfo(920001);
        var reg = await _authService.MiniAppRegisterAsync(new TelegramMiniAppRegisterRequestDto
        {
            InitData = Sign(tg),
            Name = "Mia",
            Age = 27,
            Location = "Lisbon",
            Gender = "female",
            Bio = string.Empty,
        });
        Assert.NotNull(reg);
        Assert.NotEmpty(reg!.AccessToken);
        Assert.Contains("telegram", reg.User.AuthMethods);
        Assert.DoesNotContain("local", reg.User.AuthMethods);

        // After registration, MiniAppLogin with a fresh initData for the same tg id should sign in.
        var login = await _authService.MiniAppLoginAsync(new TelegramMiniAppLoginRequestDto { InitData = Sign(tg) });
        Assert.NotNull(login);
        Assert.Equal("signedIn", login!.Status);
        Assert.NotNull(login.Auth);
    }

    [Fact]
    public async Task MiniAppRegister_InvalidInitData_ReturnsNull()
    {
        var reg = await _authService.MiniAppRegisterAsync(new TelegramMiniAppRegisterRequestDto
        {
            InitData = "bogus=1&hash=" + new string('a', 64),
            Name = "x", Age = 20, Location = "x", Gender = "male",
        });
        Assert.Null(reg);
    }

    [Fact]
    public async Task MiniAppLinkLogin_WithCorrectCredentials_AppendsTelegramToMethods()
    {
        var tg = NewTgInfo(930001);
        var linked = await _authService.MiniAppLinkLoginAsync(new TelegramMiniAppLinkLoginRequestDto
        {
            Email = "test@example.com",
            Password = "Test123!@#",
            InitData = Sign(tg),
        });
        Assert.NotNull(linked);
        Assert.Contains("telegram", linked!.User.AuthMethods);
        Assert.Contains("local", linked.User.AuthMethods);
    }

    [Fact]
    public async Task MiniAppLinkLogin_WithBadPassword_ReturnsNull()
    {
        var tg = NewTgInfo(930002);
        var linked = await _authService.MiniAppLinkLoginAsync(new TelegramMiniAppLinkLoginRequestDto
        {
            Email = "test@example.com",
            Password = "WrongPassword!1",
            InitData = Sign(tg),
        });
        Assert.Null(linked);
    }

    [Fact]
    public async Task MiniAppLogin_BotTokenUnset_ReturnsNull()
    {
        var svcWithoutBot = new MockAuthServiceHarness.Untokened();
        var res = await svcWithoutBot.Service.MiniAppLoginAsync(new TelegramMiniAppLoginRequestDto
        {
            InitData = Sign(NewTgInfo(940001)),
        });
        Assert.Null(res);
    }
}

/// <summary>Spin up a MockAuthService with an empty BotToken to exercise the 503-path.</summary>
internal static class MockAuthServiceHarness
{
    internal class Untokened
    {
        public MockAuthService Service { get; }
        public Untokened()
        {
            var jwt = new JwtService(new JwtSettings
            {
                SecretKey = "test-secret-key-min-32-characters!",
                Issuer = "TestIssuer",
                Audience = "TestAudience",
                AccessTokenLifetimeMinutes = 15,
                RefreshTokenLifetimeDays = 7
            }, NullLogger<JwtService>.Instance);
            var (app, invites, events) = TestAuthDependencies.CreateMockStack();
            Service = new MockAuthService(
                jwt,
                new PasswordHasher(),
                NullLogger<MockAuthService>.Instance,
                new NullEmailService(NullLogger<NullEmailService>.Instance),
                app,
                invites,
                events,
                Options.Create(new TelegramAuthOptions()),
                Options.Create(new GoogleAuthOptions()));
        }
    }
}
