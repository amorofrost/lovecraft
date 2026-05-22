using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Azure;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class MockAppConfigServiceTests
{
    [Fact]
    public async Task GetConfigAsync_ReturnsDefaultRankThresholds()
    {
        var service = new MockAppConfigService();
        var config = await service.GetConfigAsync();

        Assert.Equal(5, config.Ranks.ActiveReplies);
        Assert.Equal(15, config.Ranks.FriendLikes);
        Assert.Equal(100, config.Ranks.CrewReplies);
        Assert.Equal(10, config.Ranks.CrewMatches);
    }

    [Fact]
    public async Task GetConfigAsync_ReturnsDefaultPermissions()
    {
        var service = new MockAppConfigService();
        var config = await service.GetConfigAsync();

        Assert.Equal("activeMember", config.Permissions.CreateTopic);
        Assert.Equal("moderator", config.Permissions.DeleteAnyReply);
        Assert.Equal("admin", config.Permissions.AssignRole);
    }

    [Fact]
    public async Task GetConfigAsync_DefaultSendBroadcastIsAdmin()
    {
        var service = new MockAppConfigService();
        var config = await service.GetConfigAsync();

        Assert.Equal("admin", config.Permissions.SendBroadcast);
    }

    [Fact]
    public async Task GetConfigAsync_ReturnsDefaultRegistration()
    {
        var service = new MockAppConfigService();
        var config = await service.GetConfigAsync();

        Assert.False(config.Registration.RequireEventInvite);
    }

    [Fact]
    public async Task GetConfigAsync_DefaultFeedEnabledIsTrue()
    {
        var service = new MockAppConfigService();
        var config = await service.GetConfigAsync();

        Assert.True(config.Features.FeedEnabled);
    }

    [Fact]
    public async Task MockAppConfigService_SetMetricsConfig_AffectsBothGetters()
    {
        var svc = new MockAppConfigService();
        var custom = new MetricsConfig(false, true, false, true, 12, 30, 7);
        svc.SetMetricsConfig(custom);

        var direct = await svc.GetMetricsConfigAsync();
        var composite = (await svc.GetConfigAsync()).Metrics;

        Assert.Equal(custom, direct);
        Assert.Equal(custom, composite);
    }
}

public class AzureAppConfigServiceTests
{
    private static (Mock<TableServiceClient> tsc, Mock<TableClient> tc) BuildClientMocks(
        IEnumerable<AppConfigEntity> entities)
    {
        var tc = new Mock<TableClient>();
        var page = Azure.Page<AppConfigEntity>.FromValues(entities.ToList(), null, Mock.Of<Response>());
        tc.Setup(t => t.QueryAsync<AppConfigEntity>(
                It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .Returns(Azure.AsyncPageable<AppConfigEntity>.FromPages(new[] { page }));
        tc.Setup(t => t.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue<TableItem>(null!, Mock.Of<Response>()));

        var tsc = new Mock<TableServiceClient>();
        tsc.Setup(x => x.GetTableClient(TableNames.AppConfig)).Returns(tc.Object);
        return (tsc, tc);
    }

    private static AppConfigEntity Row(string pk, string rk, string val) =>
        new() { PartitionKey = pk, RowKey = rk, Value = val };

    [Fact]
    public async Task GetConfigAsync_OverridesDefaultFromTable()
    {
        var entities = new[]
        {
            Row(AppConfigEntity.PartitionRankThresholds, AppConfigKeys.RankThresholdsKeys.ActiveReplies, "10"),
            Row(AppConfigEntity.PartitionPermissions, AppConfigKeys.PermissionKeys.CreateTopic, "novice"),
        };
        var (tsc, _) = BuildClientMocks(entities);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new AzureAppConfigService(tsc.Object, cache, NullLogger<AzureAppConfigService>.Instance);

        var config = await svc.GetConfigAsync();

        Assert.Equal(10, config.Ranks.ActiveReplies);
        Assert.Equal("novice", config.Permissions.CreateTopic);
        Assert.Equal(3, config.Ranks.ActiveLikes); // not overridden → default
    }

    [Fact]
    public async Task GetConfigAsync_SecondCallUsesCache()
    {
        var (tsc, tc) = BuildClientMocks(Array.Empty<AppConfigEntity>());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new AzureAppConfigService(tsc.Object, cache, NullLogger<AzureAppConfigService>.Instance);

        await svc.GetConfigAsync();
        await svc.GetConfigAsync();

        tc.Verify(t => t.QueryAsync<AppConfigEntity>(
            It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetConfigAsync_InvalidIntValueFallsBackToDefault()
    {
        var entities = new[]
        {
            Row(AppConfigEntity.PartitionRankThresholds, AppConfigKeys.RankThresholdsKeys.ActiveReplies, "not-a-number"),
        };
        var (tsc, _) = BuildClientMocks(entities);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new AzureAppConfigService(tsc.Object, cache, NullLogger<AzureAppConfigService>.Instance);

        var config = await svc.GetConfigAsync();

        Assert.Equal(5, config.Ranks.ActiveReplies); // default
    }

    [Fact]
    public async Task GetConfigAsync_SendBroadcastDefaultsToAdminWhenNoRow()
    {
        var (tsc, _) = BuildClientMocks(Array.Empty<AppConfigEntity>());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new AzureAppConfigService(tsc.Object, cache, NullLogger<AzureAppConfigService>.Instance);

        var config = await svc.GetConfigAsync();

        Assert.Equal("admin", config.Permissions.SendBroadcast);
    }

    [Fact]
    public async Task GetConfigAsync_OverridesSendBroadcastFromTable()
    {
        var entities = new[]
        {
            Row(AppConfigEntity.PartitionPermissions, AppConfigKeys.PermissionKeys.SendBroadcast, "moderator"),
        };
        var (tsc, _) = BuildClientMocks(entities);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new AzureAppConfigService(tsc.Object, cache, NullLogger<AzureAppConfigService>.Instance);

        var config = await svc.GetConfigAsync();

        Assert.Equal("moderator", config.Permissions.SendBroadcast);
    }

    [Fact]
    public async Task GetConfigAsync_OverridesRegistrationFromTable()
    {
        var entities = new[]
        {
            Row(AppConfigEntity.PartitionRegistration, AppConfigKeys.RegistrationKeys.RequireEventInvite, "true"),
        };
        var (tsc, _) = BuildClientMocks(entities);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new AzureAppConfigService(tsc.Object, cache, NullLogger<AzureAppConfigService>.Instance);

        var config = await svc.GetConfigAsync();

        Assert.True(config.Registration.RequireEventInvite);
    }

    [Fact]
    public async Task GetConfigAsync_FeedEnabledDefaultsToTrueWhenNoRow()
    {
        var (tsc, _) = BuildClientMocks(Array.Empty<AppConfigEntity>());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new AzureAppConfigService(tsc.Object, cache, NullLogger<AzureAppConfigService>.Instance);

        var config = await svc.GetConfigAsync();

        Assert.True(config.Features.FeedEnabled);
    }

    [Fact]
    public async Task GetConfigAsync_FeedEnabledOverriddenToFalseFromTable()
    {
        var entities = new[]
        {
            Row(AppConfigEntity.PartitionFeatures, AppConfigKeys.FeatureKeys.FeedEnabled, "false"),
        };
        var (tsc, _) = BuildClientMocks(entities);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new AzureAppConfigService(tsc.Object, cache, NullLogger<AzureAppConfigService>.Instance);

        var config = await svc.GetConfigAsync();

        Assert.False(config.Features.FeedEnabled);
    }

    [Fact]
    public async Task GetConfigAsync_MetricsDefaultsWhenNoRows()
    {
        var (tsc, _) = BuildClientMocks(Array.Empty<AppConfigEntity>());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new AzureAppConfigService(tsc.Object, cache, NullLogger<AzureAppConfigService>.Instance);

        var config = await svc.GetConfigAsync();

        Assert.Equal(MetricsConfig.Defaults, config.Metrics);
    }

    [Fact]
    public async Task GetConfigAsync_MetricsFrontendPerfOverriddenToFalseFromTable()
    {
        var entities = new[]
        {
            Row(AppConfigEntity.PartitionMetrics, AppConfigKeys.MetricsKeys.FrontendPerf, "false"),
        };
        var (tsc, _) = BuildClientMocks(entities);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new AzureAppConfigService(tsc.Object, cache, NullLogger<AzureAppConfigService>.Instance);

        var config = await svc.GetConfigAsync();

        Assert.False(config.Metrics.FrontendPerf);
        Assert.True(config.Metrics.RequestTiming);   // not overridden → default
        Assert.True(config.Metrics.BiEvents);         // not overridden → default
        Assert.True(config.Metrics.ContainerStats);   // not overridden → default
        Assert.Equal(MetricsConfig.Defaults.RetentionMinuteHours, config.Metrics.RetentionMinuteHours);
        Assert.Equal(MetricsConfig.Defaults.RetentionHourDays, config.Metrics.RetentionHourDays);
        Assert.Equal(MetricsConfig.Defaults.RetentionDauDays, config.Metrics.RetentionDauDays);
    }
}
