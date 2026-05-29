using System.Net;
using System.Net.Http.Json;
using Lovecraft.Backend.Controllers.V1;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Metrics;
using Lovecraft.Common.DTOs.Metrics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
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

    [Fact]
    public void ContainerStats_RecordsOneTimingPerNonNullField()
    {
        var collector = new MockMetricsCollector();
        // users + prefs are unused by this action; safe to pass null in a direct unit call.
        var ctrl = new InternalController(null!, null!, collector);

        var result = ctrl.ContainerStats(new ContainerStatsIngestDto
        {
            Container = "telegram-bot", GcHeapMb = 22, WorkingSetMb = 98, ThreadCount = 14, CpuPercent = 3.5
        });

        Assert.IsType<NoContentResult>(result);
        var rows = collector.Snapshot().Where(r => r.Category == "container_stats").ToList();
        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, r => r.DimensionKey == "telegram-bot|gc_heap_mb");
        Assert.Contains(rows, r => r.DimensionKey == "telegram-bot|working_set_mb");
        Assert.Contains(rows, r => r.DimensionKey == "telegram-bot|thread_count");
        Assert.Contains(rows, r => r.DimensionKey == "telegram-bot|cpu_percent");
    }

    [Fact]
    public void ContainerStats_SkipsNullFields()
    {
        var collector = new MockMetricsCollector();
        var ctrl = new InternalController(null!, null!, collector);
        ctrl.ContainerStats(new ContainerStatsIngestDto { Container = "x", GcHeapMb = 10 });
        Assert.Single(collector.Snapshot(), r => r.Category == "container_stats");
    }

    [Fact]
    public void ContainerStats_BlankContainer_Returns400()
    {
        var ctrl = new InternalController(null!, null!, new MockMetricsCollector());
        Assert.IsType<BadRequestResult>(ctrl.ContainerStats(new ContainerStatsIngestDto { Container = "" }));
    }
}
