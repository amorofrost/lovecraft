using System.Linq;
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
public class PreRegistrationTests
{
    private const string BotToken = "1234567:TEST-BOT-TOKEN-FOR-PREREGISTRATION";
    private const string EventId = "preregistration-test-event";

    // Dedicated to the skippedExists-repair test below: a second fixture event, distinct
    // from EventId (shared with PreRegistrationClaimTests), so asserting its exact
    // attendee membership can't be perturbed by any other test class's imports.
    private const string RepairEventId = "preregistration-repair-test-event";

    private readonly MockEventService _events;
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
        _events = events;
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

        // Dedicated event so pre-registered attendees never perturb the shared seeded
        // events that other test classes assert exact attendee counts on.
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

        if (!MockDataStore.Events.Any(e => e.Id == RepairEventId))
        {
            MockDataStore.Events.Add(new EventDto
            {
                Id = RepairEventId,
                Title = "Pre-registration repair test event",
                Description = "Fixture event owned by PreRegistrationTests (skippedExists repair)",
                Date = new DateTime(2026, 1, 2, 18, 0, 0),
                EndDate = new DateTime(2026, 1, 2, 22, 0, 0),
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

    [Fact]
    public async Task PreRegister_SkippedExists_StillRegistersAttendeeForTargetEvent()
    {
        // Set up an account that already exists but is NOT an attendee of RepairEventId.
        // Using a normal (non-shell) registered account rather than a pre-registered shell
        // for a different event: it is the simplest way to get an existing account whose
        // userId is known in advance (AccountName is normalized identically to a Telegram
        // username by PreRegistrationRowValidator/AccountNameValidator) while guaranteeing
        // it starts out with zero event memberships, so the only way it could show up on
        // RepairEventId's roster afterwards is via the fix under test.
        const string username = "Repair_User";
        var registered = await _auth.RegisterAsync(new RegisterRequestDto
        {
            AccountName = username,
            Email = "repair_user@example.com",
            Password = "Str0ng!Passw0rd",
            Name = "Repair User",
            Age = 30,
            Gender = "female",
        });
        Assert.NotNull(registered);
        var userId = registered!.User.Id;

        var repairEvent = MockDataStore.Events.Single(e => e.Id == RepairEventId);
        Assert.DoesNotContain(userId, repairEvent.Attendees);

        var result = await _auth.PreRegisterAttendeesAsync(RepairEventId, new() { Row(username) });

        var row = Assert.Single(result.Results);
        Assert.Equal("skippedExists", row.Status);
        Assert.Equal(1, result.Summary.SkippedExists);
        Assert.Contains(userId, repairEvent.Attendees);
    }
}
