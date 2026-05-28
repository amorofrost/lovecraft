using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Azure;
using Lovecraft.Backend.Services.Caching;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Tests for GetUsersAsync filter parameters (location, account name, name, age, gender)
/// via the in-memory UserCache. Uses the same mock-construction pattern as AzureUserServiceCacheTests.
/// </summary>
public class AzureUserServiceFilterTests
{
    private static UserEntity MakeUser(
        string id, string country, string region,
        string secondaryCountry = "", string secondaryRegion = "",
        string? accountName = null,
        string? displayName = null,
        int age = 0,
        string gender = "") => new()
    {
        PartitionKey = UserEntity.GetPartitionKey(id),
        RowKey = id,
        Name = displayName ?? id,
        AccountNameDisplay = accountName ?? string.Empty,
        Age = age,
        Gender = gender,
        Country = country,
        Region = region,
        SecondaryCountry = secondaryCountry,
        SecondaryRegion = secondaryRegion,
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
        tsc.Setup(x => x.GetTableClient(TableNames.UserTelegramIndex)).Returns(tc.Object);

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

    [Fact]
    public async Task GetUsersAsync_MatchesUserViaSecondaryCountry()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "US", "California", "RU", "Москва"));
        cache.Set(MakeUser("u2", "DE", "Berlin"));

        var ru = await svc.GetUsersAsync(0, 100, country: "RU");
        Assert.Single(ru);
        Assert.Equal("u1", ru[0].Id);
    }

    [Fact]
    public async Task GetUsersAsync_MatchesUserViaSecondaryCountryAndRegion()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "US", "California", "RU", "Москва"));
        cache.Set(MakeUser("u2", "RU", "Санкт-Петербург"));
        cache.Set(MakeUser("u3", "DE", "Berlin"));

        var moscow = await svc.GetUsersAsync(0, 100, country: "RU", region: "Москва");
        Assert.Single(moscow);
        Assert.Equal("u1", moscow[0].Id);
    }

    [Fact]
    public async Task GetUsersAsync_DoesNotCrossSlotMix()
    {
        // primary RU + secondary US/Москва. Filter for RU/Москва should NOT match
        // because RU is in slot A but Москва is in slot B.
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "RU", "Санкт-Петербург", "US", "Москва"));

        var moscow = await svc.GetUsersAsync(0, 100, country: "RU", region: "Москва");
        Assert.Empty(moscow);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersByAccountName_ExactCaseInsensitive()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "RU", "Москва", accountName: "Alice99"));
        cache.Set(MakeUser("u2", "US", "California", accountName: "bob"));

        var match = await svc.GetUsersAsync(0, 100, accountName: "alice99");
        Assert.Single(match);
        Assert.Equal("u1", match[0].Id);

        var miss = await svc.GetUsersAsync(0, 100, accountName: "alice");
        Assert.Empty(miss);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersByName_SubstringCaseInsensitive()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "RU", "Москва", displayName: "Анна"));
        cache.Set(MakeUser("u2", "RU", "Москва", displayName: "Дмитрий"));
        cache.Set(MakeUser("u3", "RU", "Москва", displayName: "Annabelle"));

        var ann = await svc.GetUsersAsync(0, 100, name: "ann");
        Assert.Single(ann);
        Assert.Equal("u3", ann[0].Id);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersByAgeRange_Inclusive()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "RU", "Москва", age: 20));
        cache.Set(MakeUser("u2", "RU", "Москва", age: 25));
        cache.Set(MakeUser("u3", "RU", "Москва", age: 30));

        var range = await svc.GetUsersAsync(0, 100, minAge: 25, maxAge: 30);
        Assert.Equal(2, range.Count);
        Assert.DoesNotContain(range, u => u.Id == "u1");

        var lowerOnly = await svc.GetUsersAsync(0, 100, minAge: 25);
        Assert.Equal(2, lowerOnly.Count);

        var upperOnly = await svc.GetUsersAsync(0, 100, maxAge: 25);
        Assert.Equal(2, upperOnly.Count);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersByGender()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "RU", "Москва", gender: "Female"));
        cache.Set(MakeUser("u2", "RU", "Москва", gender: "Male"));
        cache.Set(MakeUser("u3", "RU", "Москва", gender: "NonBinary"));

        var males = await svc.GetUsersAsync(0, 100, gender: Gender.Male);
        Assert.Single(males);
        Assert.Equal("u2", males[0].Id);

        var nb = await svc.GetUsersAsync(0, 100, gender: Gender.NonBinary);
        Assert.Single(nb);
        Assert.Equal("u3", nb[0].Id);
    }

    [Fact]
    public async Task GetUsersAsync_CombinesAllFilters()
    {
        var (svc, cache) = BuildService();
        cache.Set(MakeUser("u1", "RU", "Москва", displayName: "Анна",   age: 25, gender: "Female"));
        cache.Set(MakeUser("u2", "RU", "Москва", displayName: "Анжела", age: 40, gender: "Female"));
        cache.Set(MakeUser("u3", "US", "Texas",  displayName: "Анна",   age: 25, gender: "Female"));
        cache.Set(MakeUser("u4", "RU", "Москва", displayName: "Иван",   age: 25, gender: "Male"));

        var result = await svc.GetUsersAsync(
            0, 100,
            country: "RU", region: "Москва",
            name: "анн",
            minAge: 18, maxAge: 30,
            gender: Gender.Female);

        Assert.Single(result);
        Assert.Equal("u1", result[0].Id);
    }
}
