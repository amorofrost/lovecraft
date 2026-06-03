using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Lovecraft.UnitTests;

[Collection("ChatNotificationTests")]
public class FcmRegistrationEndpointTests : IClassFixture<AclTests.TestAppFactory>
{
    private readonly AclTests.TestAppFactory _factory;
    public FcmRegistrationEndpointTests(AclTests.TestAppFactory factory) => _factory = factory;

    private HttpClient Client(string userId)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-User", userId);
        c.DefaultRequestHeaders.Add("X-Test-StaffRole", "none");
        return c;
    }

    [Fact]
    public async Task Register_then_unregister_roundtrip()
    {
        using var client = Client("fcm-u1");
        var reg = await client.PostAsJsonAsync("/api/v1/push/fcm/register",
            new { deviceId = "d1", token = "tok-abc", platform = "android", deviceModel = "Pixel 8" });
        reg.EnsureSuccessStatusCode();
        var body = await reg.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("d1", body.GetProperty("data").GetProperty("deviceId").GetString());

        var del = await client.DeleteAsync("/api/v1/push/fcm/register/d1");
        del.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Register_rejects_missing_token()
    {
        using var client = Client("fcm-u2");
        var reg = await client.PostAsJsonAsync("/api/v1/push/fcm/register",
            new { deviceId = "d2", token = "", platform = "android" });
        Assert.Equal(HttpStatusCode.BadRequest, reg.StatusCode);
    }

    [Fact]
    public async Task Register_requires_auth()
    {
        var client = _factory.CreateClient(); // no X-Test-User header
        var reg = await client.PostAsJsonAsync("/api/v1/push/fcm/register",
            new { token = "tok" });
        Assert.Equal(HttpStatusCode.Unauthorized, reg.StatusCode);
    }
}
