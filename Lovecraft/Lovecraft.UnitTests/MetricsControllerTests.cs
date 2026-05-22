using Lovecraft.Backend.Controllers.V1;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Metrics;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Lovecraft.UnitTests;

public class MetricsControllerTests
{
    [Fact]
    public async Task GetConfig_ReturnsFlagsFromAppConfig()
    {
        var appConfig = new MockAppConfigService();
        appConfig.SetMetricsConfig(new MetricsConfig(
            RequestTiming: true, BiEvents: false, ContainerStats: true, FrontendPerf: false,
            RetentionMinuteHours: 24, RetentionHourDays: 90, RetentionDauDays: 30));
        var ctrl = new MetricsController(new MockMetricsCollector(), appConfig);
        var result = await ctrl.GetConfig(default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<MetricsConfigDto>(ok.Value);
        Assert.True(dto.RequestTiming);
        Assert.False(dto.BiEvents);
        Assert.True(dto.ContainerStats);
        Assert.False(dto.FrontendPerf);
    }

    [Fact]
    public async Task PostFrontend_RecordsEachSampleWithFrontendPrefix()
    {
        var collector = new MockMetricsCollector();
        var ctrl = new MetricsController(collector, new MockAppConfigService());
        var batch = new FrontendMetricsBatchDto(new[]
        {
            new FrontendMetricSampleDto("/api/v1/users", "GET", 200, 42),
            new FrontendMetricSampleDto("/api/v1/auth/login", "POST", 401, 120),
        });
        var result = await ctrl.PostFrontend(batch);
        Assert.IsType<NoContentResult>(result);
        var rows = collector.Snapshot();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.StartsWith("frontend|", r.DimensionKey));
    }
}
