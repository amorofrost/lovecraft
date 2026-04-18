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
    public async Task GetConfigAsync_ReturnsDefaultRegistration()
    {
        var service = new MockAppConfigService();
        var config = await service.GetConfigAsync();

        Assert.False(config.Registration.RequireEventInvite);
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
}
