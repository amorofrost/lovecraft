using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Models;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Integration tests for /api/v1/admin/notifications. Reuses the AclTests.TestAppFactory
/// header-driven auth + mock backend. Each test uses a unique broadcast title so the
/// list-broadcast verification can find its own row regardless of cross-test interference.
/// </summary>
[Collection("AdminNotificationsControllerTests")]
public class AdminNotificationsControllerTests : IClassFixture<AclTests.TestAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true,
    };

    private readonly AclTests.TestAppFactory _factory;

    public AdminNotificationsControllerTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;
        // Ensure a stable, non-empty user set in the mock store so "all" audience resolves
        // to a deterministic count. We seed two minimal users with unique IDs scoped to
        // this test class so we don't collide with other test suites that touch users.
        SeedAdminTestUsers();
    }

    private static readonly string[] AdminTestUserIds =
    {
        "admin-notif-admin-1", "admin-notif-target-1", "admin-notif-target-2", "admin-notif-non-admin",
    };

    private static void SeedAdminTestUsers()
    {
        foreach (var id in AdminTestUserIds)
        {
            MockDataStore.Users.RemoveAll(u => u.Id == id);
            MockDataStore.Users.Add(new UserDto { Id = id, Name = id });
        }
    }

    [Fact]
    public async Task POST_broadcast_as_admin_returns_200_with_broadcast_dto()
    {
        using var client = _factory.CreateClientAsUser("admin-notif-admin-1", "admin");
        var title = $"unit-test-broadcast-{Guid.NewGuid():N}";
        var req = new CreateBroadcastRequestDto
        {
            Title = title,
            Body = "Hello aloevera community",
            Link = "/aloevera",
            Audience = new BroadcastAudienceDto("all", null),
        };

        var resp = await client.PostAsJsonAsync("/api/v1/admin/notifications/broadcast", req, JsonOpts);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<BroadcastDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.NotNull(body.Data);
        Assert.False(string.IsNullOrEmpty(body.Data!.Id));
        Assert.Equal(title, body.Data.Title);
        Assert.Equal("admin-notif-admin-1", body.Data.IssuedByUserId);
        // EstimatedRecipients comes from resolver — should be >= the users we seeded.
        Assert.True(body.Data.EstimatedRecipients >= 2,
            $"Expected at least 2 recipients for 'all' audience but got {body.Data.EstimatedRecipients}");
    }

    [Fact]
    public async Task POST_broadcast_as_non_admin_returns_403()
    {
        using var client = _factory.CreateClientAsUser("admin-notif-non-admin");
        var req = new CreateBroadcastRequestDto
        {
            Title = "should-fail",
            Body = "no permission",
            Audience = new BroadcastAudienceDto("all", null),
        };

        var resp = await client.PostAsJsonAsync("/api/v1/admin/notifications/broadcast", req, JsonOpts);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task POST_broadcast_as_moderator_returns_403()
    {
        using var client = _factory.CreateClientAsUser("admin-notif-non-admin", "moderator");
        var req = new CreateBroadcastRequestDto
        {
            Title = "mods-can-not",
            Body = "no permission",
            Audience = new BroadcastAudienceDto("all", null),
        };

        var resp = await client.PostAsJsonAsync("/api/v1/admin/notifications/broadcast", req, JsonOpts);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var payload = await resp.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOpts);
        Assert.Equal("ADMIN_REQUIRED", payload!.Error!.Code);
    }

    [Fact]
    public async Task POST_broadcast_with_empty_title_returns_400()
    {
        using var client = _factory.CreateClientAsUser("admin-notif-admin-1", "admin");
        var req = new CreateBroadcastRequestDto
        {
            Title = "",
            Body = "body",
            Audience = new BroadcastAudienceDto("all", null),
        };

        var resp = await client.PostAsJsonAsync("/api/v1/admin/notifications/broadcast", req, JsonOpts);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task POST_broadcast_with_empty_body_returns_400()
    {
        using var client = _factory.CreateClientAsUser("admin-notif-admin-1", "admin");
        var req = new CreateBroadcastRequestDto
        {
            Title = "title",
            Body = "",
            Audience = new BroadcastAudienceDto("all", null),
        };

        var resp = await client.PostAsJsonAsync("/api/v1/admin/notifications/broadcast", req, JsonOpts);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GET_broadcasts_lists_freshly_created_broadcast()
    {
        using var client = _factory.CreateClientAsUser("admin-notif-admin-1", "admin");
        var title = $"list-roundtrip-{Guid.NewGuid():N}";
        var createReq = new CreateBroadcastRequestDto
        {
            Title = title,
            Body = "body",
            Audience = new BroadcastAudienceDto("all", null),
        };
        var createResp = await client.PostAsJsonAsync("/api/v1/admin/notifications/broadcast", createReq, JsonOpts);
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<ApiResponse<BroadcastDto>>(JsonOpts);
        Assert.NotNull(created?.Data);

        var listResp = await client.GetAsync("/api/v1/admin/notifications/broadcasts");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listBody = await listResp.Content.ReadFromJsonAsync<ApiResponse<List<BroadcastDto>>>(JsonOpts);
        Assert.True(listBody!.Success);
        Assert.NotNull(listBody.Data);
        Assert.Contains(listBody.Data!, b => b.Id == created!.Data!.Id && b.Title == title);
    }

    [Fact]
    public async Task GET_broadcast_by_id_returns_matching_broadcast()
    {
        using var client = _factory.CreateClientAsUser("admin-notif-admin-1", "admin");
        var title = $"getbyid-{Guid.NewGuid():N}";
        var createReq = new CreateBroadcastRequestDto
        {
            Title = title,
            Body = "body",
            Audience = new BroadcastAudienceDto("all", null),
        };
        var createResp = await client.PostAsJsonAsync("/api/v1/admin/notifications/broadcast", createReq, JsonOpts);
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<ApiResponse<BroadcastDto>>(JsonOpts);
        var id = created!.Data!.Id;

        var getResp = await client.GetAsync($"/api/v1/admin/notifications/broadcasts/{id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var body = await getResp.Content.ReadFromJsonAsync<ApiResponse<BroadcastDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.Equal(id, body.Data!.Id);
        Assert.Equal(title, body.Data.Title);
    }

    [Fact]
    public async Task GET_broadcast_by_id_returns_404_for_unknown_id()
    {
        using var client = _factory.CreateClientAsUser("admin-notif-admin-1", "admin");
        var resp = await client.GetAsync("/api/v1/admin/notifications/broadcasts/nonexistent-id-xyz");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GET_broadcasts_as_non_admin_returns_403()
    {
        using var client = _factory.CreateClientAsUser("admin-notif-non-admin");
        var resp = await client.GetAsync("/api/v1/admin/notifications/broadcasts");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task POST_broadcast_attendingEvent_resolves_to_event_attendees()
    {
        using var client = _factory.CreateClientAsUser("admin-notif-admin-1", "admin");
        // Event "1" in mock store has 3 attendees (1, 2, 3)
        var req = new CreateBroadcastRequestDto
        {
            Title = $"event-bcast-{Guid.NewGuid():N}",
            Body = "concert update",
            Audience = new BroadcastAudienceDto("attendingEvent", "1"),
        };

        var resp = await client.PostAsJsonAsync("/api/v1/admin/notifications/broadcast", req, JsonOpts);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<BroadcastDto>>(JsonOpts);
        Assert.Equal(3, body!.Data!.EstimatedRecipients);
    }
}
