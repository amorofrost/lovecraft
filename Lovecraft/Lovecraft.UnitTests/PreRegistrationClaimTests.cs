using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Configuration;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Auth;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lovecraft.UnitTests;

[Collection("AuthTests")]
public class PreRegistrationClaimTests
{
    private const string BotToken = "1234567:TEST-BOT-TOKEN-FOR-CLAIM-TESTS";

    // MUST NOT be a shared seeded event id. Registering pre-registered attendees into the
    // seeded event "1" inflates its attendee count and breaks
    // AdminNotificationsControllerTests' exact-count assertion (this actually happened in
    // Task 2 and had to be fixed). Reuse the dedicated fixture event that
    // PreRegistrationTests already seeds.
    private const string EventId = "preregistration-test-event";

    private readonly MockAuthService _auth;

    public PreRegistrationClaimTests()
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

        // Seed the dedicated fixture event idempotently (the constructor runs per test).
        // Mirror the block already present in PreRegistrationTests — read that file and copy
        // its seeding block verbatim so the two fixtures stay identical.
        if (!MockDataStore.Events.Any(e => e.Id == EventId))
        {
            MockDataStore.Events.Add(new EventDto
            {
                Id = EventId,
                Title = "Pre-registration test event",
                Description = "Fixture event owned by PreRegistrationTests",
                Date = new DateTime(2026, 1, 1, 18, 0, 0),
                EndDate = new DateTime(2026, 1, 1, 22, 0, 0),
                Location = "Test",
                Capacity = 1000,
                Attendees = new List<string>(),
                Category = EventCategory.Concert,
                Price = "0",
                Organizer = "Test",
                Visibility = EventVisibility.Public,
            });
        }
    }

    private static TelegramLoginRequestDto SignedWidgetPayload(long id, string? username)
    {
        var dto = new TelegramLoginRequestDto
        {
            Id = id,
            FirstName = "Tg",
            Username = username,
            AuthDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        dto.Hash = TelegramLoginVerifier.ComputeHashForTest(BotToken, dto);
        return dto;
    }

    private static string SignedInitData(long id, string? username) =>
        TelegramInitDataValidator.BuildSigned(
            BotToken,
            new TelegramUserInfoDto { Id = id, FirstName = "Tg", Username = username },
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    [Fact]
    public async Task WidgetLogin_MatchingUsername_ClaimsShellAndSignsIn()
    {
        await _auth.PreRegisterAttendeesAsync(EventId,
            new() { new PreRegisterAttendeeDto { TelegramUsername = "Claim_Me", Name = "Claim Me" } });

        var result = await _auth.TelegramLoginAsync(SignedWidgetPayload(50001, "Claim_Me"));

        Assert.NotNull(result);
        Assert.Equal("signedIn", result!.Status);
        Assert.NotNull(result.Auth);

        var methods = await _auth.GetAuthMethodsAsync("claim_me");
        Assert.Contains(methods, m => string.Equals(m.Provider, "telegram", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WidgetLogin_ClaimedShell_SecondLoginResolvesByTelegramId()
    {
        await _auth.PreRegisterAttendeesAsync(EventId,
            new() { new PreRegisterAttendeeDto { TelegramUsername = "Twice_User", Name = "Twice" } });

        await _auth.TelegramLoginAsync(SignedWidgetPayload(50002, "Twice_User"));
        // Username omitted the second time — must still sign in via the linked numeric id.
        var second = await _auth.TelegramLoginAsync(SignedWidgetPayload(50002, null));

        Assert.NotNull(second);
        Assert.Equal("signedIn", second!.Status);
    }

    [Fact]
    public async Task WidgetLogin_UsernameMatchesNonShellAccount_DoesNotClaim()
    {
        // A normal account whose account name happens to equal a Telegram username.
        await _auth.RegisterAsync(new RegisterRequestDto
        {
            AccountName = "Real_User",
            Email = "real@example.com",
            Password = "Str0ng!Passw0rd",
            Name = "Real User",
            Age = 30,
            Gender = "male",
        });

        var result = await _auth.TelegramLoginAsync(SignedWidgetPayload(50003, "Real_User"));

        Assert.NotNull(result);
        Assert.Equal("pending", result!.Status);

        var methods = await _auth.GetAuthMethodsAsync("real_user");
        Assert.DoesNotContain(methods, m => string.Equals(m.Provider, "telegram", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WidgetLogin_NoMatchingShell_FallsThroughToPending()
    {
        var result = await _auth.TelegramLoginAsync(SignedWidgetPayload(50004, "nobody_here"));

        Assert.NotNull(result);
        Assert.Equal("pending", result!.Status);
    }

    [Fact]
    public async Task WidgetLogin_NoUsernameInPayload_FallsThroughToPending()
    {
        var result = await _auth.TelegramLoginAsync(SignedWidgetPayload(50005, null));

        Assert.NotNull(result);
        Assert.Equal("pending", result!.Status);
    }

    [Fact]
    public async Task MiniAppLogin_MatchingUsername_ClaimsShellAndSignsIn()
    {
        await _auth.PreRegisterAttendeesAsync(EventId,
            new() { new PreRegisterAttendeeDto { TelegramUsername = "Mini_Claim", Name = "Mini Claim" } });

        var result = await _auth.MiniAppLoginAsync(new TelegramMiniAppLoginRequestDto
        {
            InitData = SignedInitData(50006, "Mini_Claim")
        });

        Assert.NotNull(result);
        Assert.Equal("signedIn", result!.Status);

        var methods = await _auth.GetAuthMethodsAsync("mini_claim");
        Assert.Contains(methods, m => string.Equals(m.Provider, "telegram", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MiniAppLogin_NoMatchingShell_FallsThroughToNeedsRegistration()
    {
        var result = await _auth.MiniAppLoginAsync(new TelegramMiniAppLoginRequestDto
        {
            InitData = SignedInitData(50007, "unknown_person")
        });

        Assert.NotNull(result);
        Assert.Equal("needsRegistration", result!.Status);
    }
}
