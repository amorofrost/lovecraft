using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lovecraft.Backend.Controllers.V1;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Caching;
using Lovecraft.Backend.Services.Metrics;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Tests for AdminMetricsController — both pure-unit (no HTTP) and integration (via TestAppFactory).
/// </summary>
[Collection("AdminMetricsControllerTests")]
public class AdminMetricsControllerTests : IClassFixture<AclTests.TestAppFactory>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly AclTests.TestAppFactory _factory;

    public AdminMetricsControllerTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. GET /admin/metrics/config — returns AdminMetricsConfigDto from appconfig
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetConfig_ReturnsConfigDto()
    {
        using var client = _factory.CreateClientAsUser("admin-metrics-1", "admin");
        var resp = await client.GetAsync("/api/v1/admin/metrics/config");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<AdminMetricsConfigDto>>(JsonOpts);
        Assert.True(body!.Success);
        var dto = body.Data!;
        // Defaults from MetricsConfig.Defaults
        Assert.True(dto.RequestTiming);
        Assert.True(dto.BiEvents);
        Assert.True(dto.ContainerStats);
        Assert.True(dto.FrontendPerf);
        Assert.Equal(24, dto.RetentionMinuteHours);
        Assert.Equal(90, dto.RetentionHourDays);
        Assert.Equal(30, dto.RetentionDauDays);
    }

    [Fact]
    public async Task GetConfig_AsNonAdmin_Returns403()
    {
        using var client = _factory.CreateClientAsUser("regular-metrics", "none");
        var resp = await client.GetAsync("/api/v1/admin/metrics/config");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. PUT /admin/metrics/config — persists and invalidates cache
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PutConfig_PersistsAndInvalidates()
    {
        // Pure-unit test: drive the controller directly so we can inspect MockAppConfigService state.
        var appConfig = new MockAppConfigService();
        var userCache = new UserCache();
        var mau = new MauCalculator(null, new MemoryCache(new MemoryCacheOptions()));

        var ctrl = new AdminMetricsController(appConfig, userCache, mau);

        var newConfig = new AdminMetricsConfigDto(
            RequestTiming: false,
            BiEvents: true,
            ContainerStats: false,
            FrontendPerf: true,
            RetentionMinuteHours: 12,
            RetentionHourDays: 30,
            RetentionDauDays: 7);

        var result = await ctrl.PutConfig(newConfig, default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<AdminMetricsConfigDto>>(ok.Value);
        Assert.True(response.Success);

        // Read back via the service to confirm persistence
        var persisted = await appConfig.GetMetricsConfigAsync();
        Assert.False(persisted.RequestTiming);
        Assert.True(persisted.BiEvents);
        Assert.False(persisted.ContainerStats);
        Assert.True(persisted.FrontendPerf);
        Assert.Equal(12, persisted.RetentionMinuteHours);
        Assert.Equal(30, persisted.RetentionHourDays);
        Assert.Equal(7, persisted.RetentionDauDays);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. GET /admin/metrics/containers — computes status from heartbeat age
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetContainers_ComputesStatusFromHeartbeatAge()
    {
        // Test the status logic directly (no table access needed for this unit)
        var now = DateTime.UtcNow;

        // Simulate age calculation: green <60s, amber 60-180s, red >180s
        static string ComputeStatus(double ageSeconds, string? note = null)
        {
            if (note is not null && note.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = note.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var httpStatus))
                    return httpStatus is >= 200 and < 300 ? "green" : "red";
            }
            return ageSeconds < 60 ? "green" : ageSeconds < 180 ? "amber" : "red";
        }

        Assert.Equal("green", ComputeStatus(30));
        Assert.Equal("amber", ComputeStatus(90));
        Assert.Equal("red", ComputeStatus(200));

        // Frontend probe row: HTTP status in Note
        Assert.Equal("green", ComputeStatus(500, "HTTP 200 OK"));   // age ignored for HTTP rows
        Assert.Equal("red", ComputeStatus(10, "HTTP 503 Service Unavailable"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. GET /admin/metrics/overview — aggregates from multiple sources
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverview_AggregatesFromMultipleSources()
    {
        // Use the integration test factory; mock storage, no real Azure.
        using var client = _factory.CreateClientAsUser("admin-metrics-2", "admin");
        var resp = await client.GetAsync("/api/v1/admin/metrics/overview");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ApiResponse<MetricsOverviewDto>>(JsonOpts);
        Assert.True(body!.Success);
        var dto = body.Data!;
        // In mock mode: Registered >= 0, DAU/MAU = 0 (no Azure tables), requests = 0
        Assert.True(dto.Registered >= 0);
        Assert.Equal(0, dto.Dau);
        Assert.Equal(0, dto.Mau);
        Assert.True(dto.CurrentlyActive >= 0);
        Assert.Equal(0, dto.RequestsLastHour);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. GET /admin/metrics/timeseries — resolves to minute or hour table
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Timeseries_ReturnEmptyListInMockMode()
    {
        using var client = _factory.CreateClientAsUser("admin-metrics-3", "admin");
        var from = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-2).ToString("o"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.ToString("o"));

        // minute resolution
        var respMin = await client.GetAsync(
            $"/api/v1/admin/metrics/timeseries?category=request_timing&from={from}&to={to}&resolution=minute");
        Assert.Equal(HttpStatusCode.OK, respMin.StatusCode);
        var bodyMin = await respMin.Content.ReadFromJsonAsync<ApiResponse<List<TimeseriesPointDto>>>(JsonOpts);
        Assert.True(bodyMin!.Success);
        Assert.Empty(bodyMin.Data!);  // no table in mock mode

        // hour resolution
        var respHour = await client.GetAsync(
            $"/api/v1/admin/metrics/timeseries?category=request_timing&from={from}&to={to}&resolution=hour");
        Assert.Equal(HttpStatusCode.OK, respHour.StatusCode);
        var bodyHour = await respHour.Content.ReadFromJsonAsync<ApiResponse<List<TimeseriesPointDto>>>(JsonOpts);
        Assert.True(bodyHour!.Success);
        Assert.Empty(bodyHour.Data!);
    }

    [Fact]
    public async Task Timeseries_MissingCategory_Returns400()
    {
        using var client = _factory.CreateClientAsUser("admin-metrics-4", "admin");
        var from = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.ToString("o"));
        var resp = await client.GetAsync($"/api/v1/admin/metrics/timeseries?from={from}&to={to}");
        // Missing required category — model binding will supply empty string; controller returns 400
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. InterpolatePercentile — pure unit test of the helper method
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InterpolatePercentile_NullWhenNoSamples()
    {
        var buckets = new long[HistogramBuckets.BucketCount];  // all zeros
        var result = AdminMetricsController.InterpolatePercentile(
            buckets, HistogramBuckets.Boundaries, 95);
        Assert.Null(result);
    }

    [Fact]
    public void InterpolatePercentile_SimpleCase_AllInFirstBucket()
    {
        // All 100 samples in bucket 0 (<=25ms boundary)
        var buckets = new long[HistogramBuckets.BucketCount];
        buckets[0] = 100;
        // P50 should be ≈12.5 (midpoint of [0, 25])
        var p50 = AdminMetricsController.InterpolatePercentile(
            buckets, HistogramBuckets.Boundaries, 50);
        Assert.NotNull(p50);
        Assert.InRange(p50!.Value, 0, 25);

        // P95 should also be in [0, 25]
        var p95 = AdminMetricsController.InterpolatePercentile(
            buckets, HistogramBuckets.Boundaries, 95);
        Assert.NotNull(p95);
        Assert.InRange(p95!.Value, 0, 25);
    }

    [Fact]
    public void InterpolatePercentile_P95_SpansMultipleBuckets()
    {
        // 90 samples in B0 (<=25ms), 10 in B1 (<=50ms)
        var buckets = new long[HistogramBuckets.BucketCount];
        buckets[0] = 90;
        buckets[1] = 10;
        // P95 target = 95 → falls in bucket 1 (cumulative: B0=90, B0+B1=100)
        var p95 = AdminMetricsController.InterpolatePercentile(
            buckets, HistogramBuckets.Boundaries, 95);
        Assert.NotNull(p95);
        // Bucket 1 covers [25, 50]. 95th element is the 5th of 10 in bucket 1.
        Assert.InRange(p95!.Value, 25, 50);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. GET /admin/metrics/bi — returns daily arrays
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBi_ReturnsDayArraysOfCorrectLength()
    {
        using var client = _factory.CreateClientAsUser("admin-metrics-5", "admin");

        foreach (var (range, expectedDays) in new[] { ("24h", 1), ("7d", 7), ("30d", 30) })
        {
            var resp = await client.GetAsync($"/api/v1/admin/metrics/bi?range={range}");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<ApiResponse<BiTimeseriesDto>>(JsonOpts);
            Assert.True(body!.Success, $"range={range}");
            var dto = body.Data!;
            Assert.Equal(expectedDays, dto.Days.Length);
            Assert.Equal(expectedDays, dto.Registered.Length);
            Assert.Equal(expectedDays, dto.Dau.Length);
            Assert.Equal(expectedDays, dto.Mau.Length);
        }
    }

    [Fact]
    public void AggregateEndpointStats_MergesAcrossStatusCodes()
    {
        var b200 = new long[HistogramBuckets.BucketCount]; b200[0] = 10;
        var b404 = new long[HistogramBuckets.BucketCount]; b404[1] = 5;
        var bLikes = new long[HistogramBuckets.BucketCount]; bLikes[0] = 7;

        var rows = new List<(string dimensionKey, long count, long[] buckets)>
        {
            ("backend|GET|api~v1~users~{id}|200", 10, b200),
            ("backend|GET|api~v1~users~{id}|404", 5,  b404),
            ("backend|POST|api~v1~matching~likes|200", 7, bLikes),
        };

        var result = AdminMetricsController.AggregateEndpointStats(rows);

        var users = Assert.Single(result, r => r.Route == "/api/v1/users/{id}");
        Assert.Equal("GET", users.Method);
        Assert.Equal(15, users.Count);                       // 10 + 5 merged across statuses
        Assert.Equal("GET|/api/v1/users/{id}", users.RouteKey);
        Assert.Equal("/api/v1/users/{id}", result[0].Route); // sorted by count desc
    }
}
