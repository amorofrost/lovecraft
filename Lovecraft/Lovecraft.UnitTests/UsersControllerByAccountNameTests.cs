using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Models;

namespace Lovecraft.UnitTests;

/// <summary>
/// Integration tests for GET /api/v1/users/by-account-name/{name}.
/// Uses mock mode with test-auth headers (same pattern as AclTests / UsersControllerUpdateTests).
/// </summary>
[Collection("UsersControllerByAccountNameTests")]
public class UsersControllerByAccountNameTests : IClassFixture<AclTests.TestAppFactory>, IDisposable
{
    // Caller user — just needs a valid account-name-style ID so auth succeeds.
    private const string CallerUserId = "bylookup-caller-1";

    // User to be found — valid account-name ID (5+ chars, starts with letter).
    private const string TargetAccountName = "lookuptarget1";

    // A legacy-style ID that should NOT be found via this endpoint.
    private const string LegacyUserId = "bylookup-legacy-1"; // fails AccountNameValidator (contains hyphen)

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true,
    };

    private readonly AclTests.TestAppFactory _factory;

    public UsersControllerByAccountNameTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;

        // Seed: caller user (auth only, no lookup needed for caller)
        if (!MockDataStore.Users.Any(u => u.Id == CallerUserId))
            MockDataStore.Users.Add(new UserDto { Id = CallerUserId, Name = "Caller" });

        // Seed: target user with a valid account-name-style ID
        if (!MockDataStore.Users.Any(u => u.Id == TargetAccountName))
            MockDataStore.Users.Add(new UserDto { Id = TargetAccountName, Name = "Target User" });

        // Seed: legacy-style user with a hyphenated ID (fails AccountNameValidator)
        if (!MockDataStore.Users.Any(u => u.Id == LegacyUserId))
            MockDataStore.Users.Add(new UserDto { Id = LegacyUserId, Name = "Legacy User" });
    }

    public void Dispose()
    {
        MockDataStore.Users.RemoveAll(u =>
            u.Id == CallerUserId || u.Id == TargetAccountName || u.Id == LegacyUserId);
    }

    [Fact]
    public async Task GetByAccountName_FindsSeededUser()
    {
        using var client = _factory.CreateClientAsUser(CallerUserId);
        var resp = await client.GetAsync($"/api/v1/users/by-account-name/{TargetAccountName}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.Equal(TargetAccountName, body.Data!.Id);
    }

    [Fact]
    public async Task GetByAccountName_IsCaseInsensitive()
    {
        using var client = _factory.CreateClientAsUser(CallerUserId);
        // Lookup with mixed case — should still find the canonical lowercase entry.
        var resp = await client.GetAsync($"/api/v1/users/by-account-name/LookupTarget1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.Equal(TargetAccountName, body.Data!.Id);
    }

    [Fact]
    public async Task GetByAccountName_ReturnsNotFoundForUnknownName()
    {
        using var client = _factory.CreateClientAsUser(CallerUserId);
        var resp = await client.GetAsync("/api/v1/users/by-account-name/nobody_here_xx");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal("USER_NOT_FOUND", body!.Error?.Code);
    }

    [Fact]
    public async Task GetByAccountName_ReturnsNotFoundForLegacyHyphenatedId()
    {
        // "bylookup-legacy-1" is in MockDataStore.Users but has a hyphenated ID that
        // fails AccountNameValidator — the endpoint must not expose it.
        using var client = _factory.CreateClientAsUser(CallerUserId);
        // The name itself is invalid format (hyphens), so validator rejects it → 404.
        var resp = await client.GetAsync($"/api/v1/users/by-account-name/{LegacyUserId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetByAccountName_ReturnsUnauthorizedWithNoAuth()
    {
        using var client = _factory.CreateClient(); // no X-Test-User header
        var resp = await client.GetAsync($"/api/v1/users/by-account-name/{TargetAccountName}");
        // Controller is [Authorize], so unauthenticated requests are rejected.
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetByAccountName_ReturnsNotFoundForInvalidFormat()
    {
        using var client = _factory.CreateClientAsUser(CallerUserId);
        // "ab" is too short (< 5 chars), fails AccountNameValidator → 404.
        var resp = await client.GetAsync("/api/v1/users/by-account-name/ab");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
