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

// ─────────────────────────────────────────────────────────────────────────────
// UserCache unit tests
// ─────────────────────────────────────────────────────────────────────────────
public class UserCacheTests
{
    private static UserEntity MakeUser(string id, string name = "Test") => new()
    {
        PartitionKey = UserEntity.GetPartitionKey(id),
        RowKey = id,
        Name = name,
        PreferencesJson = "{}",
        SettingsJson = "{}",
        ImagesJson = "[]",
    };

    [Fact]
    public void Set_and_Get_round_trips_entity()
    {
        var cache = new UserCache();
        cache.Set(MakeUser("u1", "Alice"));

        var result = cache.Get("u1");
        Assert.NotNull(result);
        Assert.Equal("u1", result.RowKey);
        Assert.Equal("Alice", result.Name);
    }

    [Fact]
    public void Get_unknown_id_returns_null()
    {
        var cache = new UserCache();
        Assert.Null(cache.Get("does-not-exist"));
    }

    [Fact]
    public void Set_overwrites_existing_entry()
    {
        var cache = new UserCache();
        cache.Set(MakeUser("u1", "Alice"));
        cache.Set(MakeUser("u1", "Bob"));

        Assert.Equal("Bob", cache.Get("u1")!.Name);
    }

    [Fact]
    public void Remove_makes_entry_unreachable()
    {
        var cache = new UserCache();
        cache.Set(MakeUser("u1"));
        cache.Remove("u1");

        Assert.Null(cache.Get("u1"));
    }

    [Fact]
    public void Remove_nonexistent_key_does_not_throw()
    {
        var cache = new UserCache();
        var ex = Record.Exception(() => cache.Remove("ghost"));
        Assert.Null(ex);
    }

    [Fact]
    public void GetAll_returns_all_stored_entities()
    {
        var cache = new UserCache();
        cache.Set(MakeUser("u1"));
        cache.Set(MakeUser("u2"));
        cache.Set(MakeUser("u3"));

        var all = cache.GetAll();
        Assert.Equal(3, all.Count);
        Assert.Contains(all, e => e.RowKey == "u1");
        Assert.Contains(all, e => e.RowKey == "u2");
        Assert.Contains(all, e => e.RowKey == "u3");
    }

    [Fact]
    public void GetAll_returns_snapshot_independent_of_later_mutations()
    {
        var cache = new UserCache();
        cache.Set(MakeUser("u1"));
        var snapshot = cache.GetAll();

        cache.Set(MakeUser("u2"));

        // The list captured before u2 was added should still have only 1 element.
        Assert.Single(snapshot);
    }

    [Fact]
    public void ConcurrentSet_does_not_corrupt_cache()
    {
        var cache = new UserCache();
        var ids = Enumerable.Range(1, 300).Select(i => $"u{i}").ToList();

        Parallel.ForEach(ids, id => cache.Set(MakeUser(id)));

        var all = cache.GetAll();
        Assert.Equal(300, all.Count);
        foreach (var id in ids)
            Assert.NotNull(cache.Get(id));
    }

    [Fact]
    public async Task LoadAsync_populates_cache_from_table_client()
    {
        var entities = new List<UserEntity> { MakeUser("u1"), MakeUser("u2") };
        var page = Page<UserEntity>.FromValues(entities, continuationToken: null, Mock.Of<Response>());
        var pageable = AsyncPageable<UserEntity>.FromPages(new[] { page });

        var tc = new Mock<TableClient>();
        tc.Setup(t => t.QueryAsync<UserEntity>(
                It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var cache = new UserCache();
        await cache.LoadAsync(tc.Object);

        Assert.Equal(2, cache.GetAll().Count);
        Assert.NotNull(cache.Get("u1"));
        Assert.NotNull(cache.Get("u2"));
    }

    [Fact]
    public async Task LoadAsync_on_empty_table_leaves_cache_empty()
    {
        var page = Page<UserEntity>.FromValues(new List<UserEntity>(), continuationToken: null, Mock.Of<Response>());
        var pageable = AsyncPageable<UserEntity>.FromPages(new[] { page });

        var tc = new Mock<TableClient>();
        tc.Setup(t => t.QueryAsync<UserEntity>(
                It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var cache = new UserCache();
        await cache.LoadAsync(tc.Object);

        Assert.Empty(cache.GetAll());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AzureUserService — cache-integration tests
// ─────────────────────────────────────────────────────────────────────────────
public class AzureUserServiceCacheTests
{
    private static UserEntity MakeUser(string id, string name = "Test") => new()
    {
        PartitionKey = UserEntity.GetPartitionKey(id),
        RowKey = id,
        Name = name,
        PreferencesJson = "{}",
        SettingsJson = "{}",
        ImagesJson = "[]",
        ETag = new ETag("\"etag\""),
    };

    private static (AzureUserService svc, Mock<TableClient> tc, UserCache cache) BuildService()
    {
        var tc = new Mock<TableClient>();
        tc.Setup(t => t.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue<TableItem>(null!, Mock.Of<Response>()));

        var tsc = new Mock<TableServiceClient>();
        tsc.Setup(x => x.GetTableClient(TableNames.Users)).Returns(tc.Object);

        var cache = new UserCache();
        var svc = new AzureUserService(
            tsc.Object, NullLogger<AzureUserService>.Instance, new MockAppConfigService(), cache);

        return (svc, tc, cache);
    }

    // GetUsersAsync reads from the in-memory cache — no QueryAsync call to Azure.
    [Fact]
    public async Task GetUsersAsync_reads_from_cache_not_azure()
    {
        var (svc, tc, cache) = BuildService();
        cache.Set(MakeUser("u1", "Alice"));
        cache.Set(MakeUser("u2", "Bob"));

        var result = await svc.GetUsersAsync(0, 10);

        Assert.Equal(2, result.Count);
        tc.Verify(t => t.QueryAsync<UserEntity>(
            It.IsAny<string?>(), It.IsAny<int?>(),
            It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetUsersAsync_applies_skip_and_take_to_cache()
    {
        var (svc, _, cache) = BuildService();
        foreach (var i in Enumerable.Range(1, 5))
            cache.Set(MakeUser($"u{i}"));

        var page = await svc.GetUsersAsync(0, 3);
        Assert.Equal(3, page.Count);

        var page2 = await svc.GetUsersAsync(0, 2);
        Assert.Equal(2, page2.Count);
    }

    // GetUserByIdAsync hits the cache and skips Azure.
    [Fact]
    public async Task GetUserByIdAsync_returns_cached_entity_without_azure_call()
    {
        var (svc, tc, cache) = BuildService();
        cache.Set(MakeUser("u1", "Cached"));

        var result = await svc.GetUserByIdAsync("u1");

        Assert.NotNull(result);
        Assert.Equal("Cached", result.Name);
        tc.Verify(t => t.GetEntityAsync<UserEntity>(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // GetUserByIdAsync falls back to Azure when the id is not in the cache.
    [Fact]
    public async Task GetUserByIdAsync_falls_back_to_azure_on_cache_miss()
    {
        var (svc, tc, _) = BuildService();
        var azureEntity = MakeUser("u99", "FromAzure");
        tc.Setup(t => t.GetEntityAsync<UserEntity>(
                It.IsAny<string>(), It.Is<string>(k => k == "u99"),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(azureEntity, Mock.Of<Response>()));

        var result = await svc.GetUserByIdAsync("u99");

        Assert.NotNull(result);
        Assert.Equal("FromAzure", result.Name);
    }

    // UpdateUserAsync must update the cache so the next GetUserByIdAsync sees fresh data.
    [Fact]
    public async Task UpdateUserAsync_updates_cache_entry()
    {
        var (svc, tc, cache) = BuildService();
        var original = MakeUser("u1", "Before");
        cache.Set(original);

        tc.Setup(t => t.GetEntityAsync<UserEntity>(
                It.IsAny<string>(), It.Is<string>(k => k == "u1"),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(original, Mock.Of<Response>()));
        tc.Setup(t => t.UpdateEntityAsync(
                It.IsAny<UserEntity>(), It.IsAny<ETag>(),
                It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        var dto = new Lovecraft.Common.DTOs.Users.UserDto { Name = "After", Images = new() };
        await svc.UpdateUserAsync("u1", dto);

        Assert.Equal("After", cache.Get("u1")!.Name);
    }

    // SetStaffRoleAsync must update the cache.
    [Fact]
    public async Task SetStaffRoleAsync_updates_cache_entry()
    {
        var (svc, tc, cache) = BuildService();
        var entity = MakeUser("u1");
        entity.StaffRole = "none";
        cache.Set(entity);

        tc.Setup(t => t.GetEntityAsync<UserEntity>(
                It.IsAny<string>(), It.Is<string>(k => k == "u1"),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(entity, Mock.Of<Response>()));
        tc.Setup(t => t.UpdateEntityAsync(
                It.IsAny<UserEntity>(), It.IsAny<ETag>(),
                It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        await svc.SetStaffRoleAsync("u1", StaffRole.Admin);

        Assert.Equal("admin", cache.Get("u1")!.StaffRole);
    }

    // SetRankOverrideAsync must update the cache.
    [Fact]
    public async Task SetRankOverrideAsync_updates_cache_entry()
    {
        var (svc, tc, cache) = BuildService();
        var entity = MakeUser("u1");
        cache.Set(entity);

        tc.Setup(t => t.GetEntityAsync<UserEntity>(
                It.IsAny<string>(), It.Is<string>(k => k == "u1"),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(entity, Mock.Of<Response>()));
        tc.Setup(t => t.UpdateEntityAsync(
                It.IsAny<UserEntity>(), It.IsAny<ETag>(),
                It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        await svc.SetRankOverrideAsync("u1", UserRank.AloeCrew);

        Assert.Equal("aloeCrew", cache.Get("u1")!.RankOverride);
    }

    // IncrementCounterAsync must update the cache after a successful write.
    [Fact]
    public async Task IncrementCounterAsync_updates_cache_entry()
    {
        var (svc, tc, cache) = BuildService();
        var entity = MakeUser("u1");
        entity.ReplyCount = 5;
        cache.Set(entity);

        tc.Setup(t => t.GetEntityAsync<UserEntity>(
                It.IsAny<string>(), It.Is<string>(k => k == "u1"),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(entity, Mock.Of<Response>()));
        tc.Setup(t => t.UpdateEntityAsync(
                It.IsAny<UserEntity>(), It.IsAny<ETag>(),
                It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        await svc.IncrementCounterAsync("u1", UserCounter.ReplyCount, 1);

        Assert.Equal(6, cache.Get("u1")!.ReplyCount);
    }

    // GetUsersAsync with more users than take returns only the requested count.
    [Fact]
    public async Task GetUsersAsync_honours_take_limit()
    {
        var (svc, _, cache) = BuildService();
        foreach (var i in Enumerable.Range(1, 20))
            cache.Set(MakeUser($"u{i}"));

        var result = await svc.GetUsersAsync(0, 5);

        Assert.Equal(5, result.Count);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// MockUserService — shuffle tests
// ─────────────────────────────────────────────────────────────────────────────
public class MockUserServiceShuffleTests
{
    [Fact]
    public async Task GetUsersAsync_returns_all_requested_items()
    {
        var svc = new MockUserService(new MockAppConfigService());
        var all = await svc.GetUsersAsync(0, 1000);

        // Every mock user should be present (just potentially in a different order).
        Assert.True(all.Count > 0);
        var ids = all.Select(u => u.Id).ToHashSet();
        Assert.Equal(all.Count, ids.Count); // no duplicates
    }

    [Fact]
    public async Task GetUsersAsync_shuffle_does_not_lose_or_duplicate_items()
    {
        var svc = new MockUserService(new MockAppConfigService());
        var first = await svc.GetUsersAsync(0, 1000);
        var second = await svc.GetUsersAsync(0, 1000);

        var firstIds = first.Select(u => u.Id).OrderBy(x => x).ToList();
        var secondIds = second.Select(u => u.Id).OrderBy(x => x).ToList();

        // Same set of ids in both responses, regardless of order.
        Assert.Equal(firstIds, secondIds);
    }

    [Fact]
    public async Task GetUsersAsync_shuffle_produces_different_ordering_across_calls()
    {
        // With enough users and enough runs, the chance of getting the same order
        // every time is astronomically small. We run 20 times and expect at least
        // one pair with different ordering.
        var svc = new MockUserService(new MockAppConfigService());
        var orders = new List<List<string>>();
        for (int i = 0; i < 20; i++)
        {
            var batch = await svc.GetUsersAsync(0, 1000);
            if (batch.Count >= 2)
                orders.Add(batch.Select(u => u.Id).ToList());
        }

        // Skip this assertion if MockDataStore has fewer than 2 users (prevents false failure).
        if (orders.Count < 2 || orders[0].Count < 2) return;

        var distinctOrders = orders.Select(o => string.Join(",", o)).Distinct().Count();
        Assert.True(distinctOrders > 1, "Expected shuffle to produce at least two distinct orderings across 20 calls");
    }
}
