using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Azure;
using Lovecraft.Backend.Services.Caching;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Tests for GetUsersAsync country + region filter via the in-memory UserCache.
/// Uses the same mock-construction pattern as AzureUserServiceCacheTests.
/// </summary>
public class AzureUserServiceFilterTests
{
    private static UserEntity MakeUser(string id, string country, string region) => new()
    {
        PartitionKey = UserEntity.GetPartitionKey(id),
        RowKey = id,
        Name = id,
        Country = country,
        Region = region,
        StaffRole = "none",
        PreferencesJson = "{}",
        SettingsJson = "{}",
        ImagesJson = "[]",
    };

    private static (AzureUserService svc, UserCache cache) BuildService()
    {
        var tc = new Mock<TableClient>();
        tc.Setup(t => t.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue<TableItem>(null!, Mock.Of<Response>()));

        var tsc = new Mock<TableServiceClient>();
        tsc.Setup(x => x.GetTableClient(TableNames.Users)).Returns(tc.Object);

        var cache = new UserCache();
        var svc = new AzureUserService(
            tsc.Object,
            NullLogger<AzureUserService>.Instance,
            new MockAppConfigService(),
            cache);

        return (svc, cache);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersByCountry()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "RU", "Москва"));
        cache.Set(MakeUser("u2", "RU", "Санкт-Петербург"));
        cache.Set(MakeUser("u3", "US", "California"));

        var ru = await svc.GetUsersAsync(0, 100, country: "RU");

        Assert.Equal(2, ru.Count);
        Assert.All(ru, u => Assert.Equal("RU", u.Country));
    }

    [Fact]
    public async Task GetUsersAsync_FiltersByCountryAndRegion()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "RU", "Москва"));
        cache.Set(MakeUser("u2", "RU", "Санкт-Петербург"));
        cache.Set(MakeUser("u3", "US", "California"));

        var moscow = await svc.GetUsersAsync(0, 100, country: "RU", region: "Москва");

        Assert.Single(moscow);
        Assert.Equal("u1", moscow[0].Id);
    }

    [Fact]
    public async Task GetUsersAsync_CountryFilterIsCaseInsensitive()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "RU", "Москва"));
        cache.Set(MakeUser("u2", "US", "California"));

        var ru = await svc.GetUsersAsync(0, 100, country: "ru");

        Assert.Single(ru);
        Assert.Equal("RU", ru[0].Country);
    }

    [Fact]
    public async Task GetUsersAsync_EmptyFilter_ReturnsAll()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "RU", "Москва"));
        cache.Set(MakeUser("u2", "US", "California"));

        var all = await svc.GetUsersAsync(0, 100);

        Assert.Equal(2, all.Count);
    }
}
