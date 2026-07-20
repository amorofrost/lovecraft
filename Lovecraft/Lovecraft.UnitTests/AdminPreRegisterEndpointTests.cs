using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Admin;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Models;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Integration tests for POST /api/v1/admin/events/{eventId}/preregister
/// (Lovecraft.Backend/Controllers/V1/AdminController.cs:126-150).
///
/// This endpoint bulk-creates shell accounts via IAuthService.PreRegisterAttendeesAsync,
/// which internally calls IEventService.RegisterForEventAsync and discards the bool result.
/// RegisterForEventAsync returns false for a missing or archived event, so without the
/// controller's pre-flight EVENT_NOT_FOUND / EVENT_ARCHIVED checks, importing against a
/// stale event id would report every row as "created" while registering nobody. These
/// tests pin that pre-flight behavior plus the input guards, the non-admin 403, and the
/// happy path.
///
/// Collection: this test both drives HTTP through TestAppFactory AND (on the happy-path /
/// no-accounts-created assertions) causes MockAuthService to create real shell accounts.
/// MockAuthService keys its users in static, process-wide dictionaries, so any test that
/// exercises that write path must be serialized with the other MockAuthService-touching
/// suites (PreRegistrationTests, PreRegistrationClaimTests, AuthenticationTests, etc.),
/// which all use [Collection("AuthTests")]. Following that pattern here (rather than
/// AdminNotificationsControllerTests' own dedicated collection, which never touches
/// MockAuthService) avoids racing those suites.
/// </summary>
[Collection("AuthTests")]
public class AdminPreRegisterEndpointTests : IClassFixture<AclTests.TestAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const string ValidEventId = "prereg-endpoint-valid-event";
    private const string ArchivedEventId = "prereg-endpoint-archived-event";

    private readonly AclTests.TestAppFactory _factory;

    public AdminPreRegisterEndpointTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;
        SeedEvents();
    }

    /// <summary>
    /// Dedicated fixture events, seeded idempotently under ids unique to this test class.
    /// Deliberately NOT reusing "1" (asserted to have exactly 3 attendees by
    /// AdminNotificationsControllerTests) or "preregistration-test-event" (owned by
    /// PreRegistrationTests) so this suite cannot pollute either.
    /// </summary>
    private static void SeedEvents()
    {
        if (!MockDataStore.Events.Any(e => e.Id == ValidEventId))
        {
            MockDataStore.Events.Add(new EventDto
            {
                Id = ValidEventId,
                Title = "Admin pre-register endpoint test event",
                Description = "Fixture event owned by AdminPreRegisterEndpointTests",
                Date = new DateTime(2026, 2, 1, 18, 0, 0),
                EndDate = new DateTime(2026, 2, 1, 22, 0, 0),
                Location = "Test",
                Capacity = 1000,
                Attendees = new List<string>(),
                Category = EventCategory.Concert,
                Price = "0",
                Organizer = "Test",
                Visibility = EventVisibility.Public,
                Archived = false,
            });
        }

        if (!MockDataStore.Events.Any(e => e.Id == ArchivedEventId))
        {
            MockDataStore.Events.Add(new EventDto
            {
                Id = ArchivedEventId,
                Title = "Admin pre-register endpoint archived test event",
                Description = "Fixture archived event owned by AdminPreRegisterEndpointTests",
                Date = new DateTime(2026, 2, 1, 18, 0, 0),
                EndDate = new DateTime(2026, 2, 1, 22, 0, 0),
                Location = "Test",
                Capacity = 1000,
                Attendees = new List<string>(),
                Category = EventCategory.Concert,
                Price = "0",
                Organizer = "Test",
                Visibility = EventVisibility.Public,
                Archived = true,
            });
        }
    }

    private static PreRegisterAttendeesRequestDto Request(string username, string name = "Test Person") =>
        new()
        {
            Attendees = new List<PreRegisterAttendeeDto>
            {
                new() { TelegramUsername = username, Name = name, Gender = "female" },
            },
        };

    [Fact]
    public async Task POST_preregister_unknownEvent_returns400_EVENT_NOT_FOUND()
    {
        using var client = _factory.CreateClientAsUser("prereg-endpoint-admin-1", "admin");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/admin/events/no-such-event-xyz/preregister",
            Request("Preflight_Reject"),
            JsonOpts);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PreRegisterResultDto>>(JsonOpts);
        Assert.False(body!.Success);
        Assert.Equal("EVENT_NOT_FOUND", body.Error!.Code);
    }

    [Fact]
    public async Task POST_preregister_archivedEvent_returns400_EVENT_ARCHIVED()
    {
        using var client = _factory.CreateClientAsUser("prereg-endpoint-admin-1", "admin");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/admin/events/{ArchivedEventId}/preregister",
            Request("Preflight_Archived"),
            JsonOpts);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PreRegisterResultDto>>(JsonOpts);
        Assert.False(body!.Success);
        Assert.Equal("EVENT_ARCHIVED", body.Error!.Code);
    }

    [Fact]
    public async Task POST_preregister_rejectedByPreflight_createsNoAccount()
    {
        using var client = _factory.CreateClientAsUser("prereg-endpoint-admin-1", "admin");

        // First: reject against a non-existent event. If the pre-flight guard were
        // missing/bypassed, this would silently create the "preflight_norows" shell account
        // while registering nobody (RegisterForEventAsync no-ops for a missing event).
        var rejected = await client.PostAsJsonAsync(
            "/api/v1/admin/events/no-such-event-for-norows-check/preregister",
            Request("Preflight_NoRows"),
            JsonOpts);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        // Then: post the SAME attendee against a valid event. If the earlier call had
        // created the account, this would come back "skippedExists" instead of "created".
        var accepted = await client.PostAsJsonAsync(
            $"/api/v1/admin/events/{ValidEventId}/preregister",
            Request("Preflight_NoRows"),
            JsonOpts);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<ApiResponse<PreRegisterResultDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.Equal(1, body.Data!.Summary.Created);
        var row = Assert.Single(body.Data.Results);
        Assert.Equal("created", row.Status);
    }

    [Fact]
    public async Task POST_preregister_emptyAttendees_returns400_ATTENDEES_REQUIRED()
    {
        using var client = _factory.CreateClientAsUser("prereg-endpoint-admin-1", "admin");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/admin/events/{ValidEventId}/preregister",
            new PreRegisterAttendeesRequestDto { Attendees = new List<PreRegisterAttendeeDto>() },
            JsonOpts);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PreRegisterResultDto>>(JsonOpts);
        Assert.False(body!.Success);
        Assert.Equal("ATTENDEES_REQUIRED", body.Error!.Code);
    }

    [Fact]
    public async Task POST_preregister_asNonAdmin_returns403()
    {
        using var client = _factory.CreateClientAsUser("prereg-endpoint-non-admin");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/admin/events/{ValidEventId}/preregister",
            Request("Preflight_NonAdmin"),
            JsonOpts);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task POST_preregister_validEventAndAttendee_returns200_created()
    {
        using var client = _factory.CreateClientAsUser("prereg-endpoint-admin-1", "admin");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/admin/events/{ValidEventId}/preregister",
            Request("Preflight_HappyPath"),
            JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<PreRegisterResultDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.Equal(1, body.Data!.Summary.Created);
        var row = Assert.Single(body.Data.Results);
        Assert.Equal("created", row.Status);
    }
}
