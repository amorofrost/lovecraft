using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lovecraft.UnitTests;

/// <summary>
/// Integration tests for /api/v1/notifications and /api/v1/push via WebApplicationFactory.
/// Uses the same TestAppFactory + header-driven auth scheme as AclTests.
/// </summary>
[Collection("NotificationsControllerTests")]
public class NotificationsControllerTests : IClassFixture<AclTests.TestAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true,
    };

    private readonly AclTests.TestAppFactory _factory;

    public NotificationsControllerTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GET_list_returns_empty_for_new_user()
    {
        using var client = _factory.CreateClientAsUser("notif-test-new-user-list");
        var resp = await client.GetAsync("/api/v1/notifications");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<NotificationListResponseDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.NotNull(body.Data);
        Assert.Empty(body.Data!.Items);
        Assert.Null(body.Data.NextCursor);
    }

    [Fact]
    public async Task GET_unread_count_returns_zero_for_new_user()
    {
        using var client = _factory.CreateClientAsUser("notif-test-new-user-count");
        var resp = await client.GetAsync("/api/v1/notifications/unread-count");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<UnreadCountResponseDto>>(JsonOpts);
        Assert.True(body!.Success);
        Assert.Equal(0, body.Data!.Count);
    }

    [Fact]
    public async Task POST_subscribe_then_DELETE_round_trip()
    {
        using var client = _factory.CreateClientAsUser("notif-test-push-user");

        // Subscribe
        var subscribeReq = new WebPushSubscriptionRequestDto
        {
            DeviceId = "test-device-001",
            Endpoint = "https://push.example.com/endpoint",
            P256dh = "test-p256dh-key",
            Auth = "test-auth-secret",
            UserAgent = "TestBrowser/1.0",
        };
        var subResp = await client.PostAsJsonAsync("/api/v1/push/subscribe", subscribeReq, JsonOpts);
        Assert.Equal(HttpStatusCode.OK, subResp.StatusCode);
        var subBody = await subResp.Content.ReadFromJsonAsync<ApiResponse<WebPushSubscriptionDto>>(JsonOpts);
        Assert.True(subBody!.Success);
        Assert.Equal("test-device-001", subBody.Data!.DeviceId);
        Assert.Equal("https://push.example.com/endpoint", subBody.Data.Endpoint);

        // Unsubscribe
        var delResp = await client.DeleteAsync("/api/v1/push/subscribe/test-device-001");
        Assert.Equal(HttpStatusCode.OK, delResp.StatusCode);
        var delBody = await delResp.Content.ReadFromJsonAsync<ApiResponse<object>>(JsonOpts);
        Assert.True(delBody!.Success);

        // Second delete should 404
        var del2Resp = await client.DeleteAsync("/api/v1/push/subscribe/test-device-001");
        Assert.Equal(HttpStatusCode.NotFound, del2Resp.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_when_no_token()
    {
        // A plain client with no X-Test-User header — TestAuthHandler returns NoResult,
        // so the JWT bearer fallback also finds no token → 401.
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var resp = await client.GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
