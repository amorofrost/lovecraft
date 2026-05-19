using System.Net;
using System.Net.Http.Json;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lovecraft.UnitTests;

public class InternalControllerTests : IClassFixture<AclTests.TestAppFactory>
{
    private readonly AclTests.TestAppFactory _factory;

    public InternalControllerTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;
        MockDataStore.NotificationPreferences.Clear();
        MockDataStore.UserTelegramIndex.Clear();
        Environment.SetEnvironmentVariable("INTERNAL_SERVICE_TOKEN", "test-service-token-abc123");
    }

    [Fact]
    public async Task Missing_header_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/internal/notifications/mute-type",
            new Lovecraft.Common.DTOs.Notifications.InternalMuteTypeRequestDto
            {
                TelegramUserId = "1234",
                Type = "messageReceived"
            });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Wrong_token_returns_401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "wrong-token");
        var resp = await client.PostAsJsonAsync("/api/v1/internal/notifications/mute-type",
            new Lovecraft.Common.DTOs.Notifications.InternalMuteTypeRequestDto
            {
                TelegramUserId = "1234",
                Type = "messageReceived"
            });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Unknown_telegram_user_returns_404()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "test-service-token-abc123");
        var resp = await client.PostAsJsonAsync("/api/v1/internal/notifications/mute-type",
            new Lovecraft.Common.DTOs.Notifications.InternalMuteTypeRequestDto
            {
                TelegramUserId = "9999999",
                Type = "messageReceived"
            });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Valid_token_flips_matrix_to_false()
    {
        var telegramId = "555111";
        var userId = "user-abc";
        MockDataStore.UserTelegramIndex[telegramId] = userId;

        // Pre-seed prefs with Telegram on for messageReceived
        var prefSvc = _factory.Services.GetRequiredService<INotificationPreferenceService>();
        var prefs = await prefSvc.GetPreferencesAsync(userId);
        prefs.Matrix["messageReceived"]["telegram"] = true;
        await prefSvc.UpdatePreferencesAsync(userId, prefs);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "test-service-token-abc123");
        var resp = await client.PostAsJsonAsync("/api/v1/internal/notifications/mute-type",
            new Lovecraft.Common.DTOs.Notifications.InternalMuteTypeRequestDto
            {
                TelegramUserId = telegramId,
                Type = "messageReceived"
            });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await prefSvc.GetPreferencesAsync(userId);
        Assert.False(after.Matrix["messageReceived"]["telegram"]);
    }
}
