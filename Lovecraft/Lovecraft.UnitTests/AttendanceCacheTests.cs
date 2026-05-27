using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Services.Caching;
using Lovecraft.Backend.Storage.Entities;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class AttendanceCacheTests
{
    private static EventAttendeeEntity MakeRow(string eventId, string userId) => new()
    {
        PartitionKey = eventId,
        RowKey = userId,
        RegisteredAt = DateTime.UtcNow,
    };

    [Fact]
    public void GetForUser_returns_empty_for_unknown_user()
    {
        var cache = new AttendanceCache();
        var list = cache.GetForUser("ghost");
        Assert.NotNull(list);
        Assert.Empty(list);
    }

    [Fact]
    public void AddAttendance_records_event_for_user()
    {
        var cache = new AttendanceCache();
        cache.AddAttendance("u1", "evt-1");

        Assert.Contains("evt-1", cache.GetForUser("u1"));
    }

    [Fact]
    public void AddAttendance_is_idempotent()
    {
        var cache = new AttendanceCache();
        cache.AddAttendance("u1", "evt-1");
        cache.AddAttendance("u1", "evt-1");
        cache.AddAttendance("u1", "evt-1");

        Assert.Single(cache.GetForUser("u1"));
    }

    [Fact]
    public void AddAttendance_appends_multiple_events()
    {
        var cache = new AttendanceCache();
        cache.AddAttendance("u1", "evt-1");
        cache.AddAttendance("u1", "evt-2");
        cache.AddAttendance("u1", "evt-3");

        var list = cache.GetForUser("u1");
        Assert.Equal(3, list.Count);
        Assert.Contains("evt-1", list);
        Assert.Contains("evt-2", list);
        Assert.Contains("evt-3", list);
    }

    [Fact]
    public void RemoveAttendance_drops_matching_event()
    {
        var cache = new AttendanceCache();
        cache.AddAttendance("u1", "evt-1");
        cache.AddAttendance("u1", "evt-2");

        cache.RemoveAttendance("u1", "evt-1");

        var list = cache.GetForUser("u1");
        Assert.Single(list);
        Assert.Contains("evt-2", list);
    }

    [Fact]
    public void RemoveAttendance_unknown_user_does_not_throw()
    {
        var cache = new AttendanceCache();
        var ex = Record.Exception(() => cache.RemoveAttendance("ghost", "evt-1"));
        Assert.Null(ex);
    }

    [Fact]
    public void RemoveAttendance_does_not_create_empty_entry_for_unknown_user()
    {
        var cache = new AttendanceCache();
        cache.RemoveAttendance("ghost", "evt-1");

        // The cache should remain empty — RemoveAttendance must not pollute it.
        Assert.Equal(0, cache.UserCount);
    }

    [Fact]
    public void RemoveAttendance_unknown_event_is_noop()
    {
        var cache = new AttendanceCache();
        cache.AddAttendance("u1", "evt-1");
        cache.RemoveAttendance("u1", "evt-99");

        Assert.Single(cache.GetForUser("u1"));
        Assert.Contains("evt-1", cache.GetForUser("u1"));
    }

    [Fact]
    public void RemoveEvent_drops_event_from_every_user_list()
    {
        var cache = new AttendanceCache();
        cache.AddAttendance("u1", "evt-1");
        cache.AddAttendance("u1", "evt-2");
        cache.AddAttendance("u2", "evt-1");
        cache.AddAttendance("u3", "evt-1");
        cache.AddAttendance("u3", "evt-2");

        cache.RemoveEvent("evt-1");

        Assert.DoesNotContain("evt-1", cache.GetForUser("u1"));
        Assert.DoesNotContain("evt-1", cache.GetForUser("u2"));
        Assert.DoesNotContain("evt-1", cache.GetForUser("u3"));
        Assert.Contains("evt-2", cache.GetForUser("u1"));
        Assert.Contains("evt-2", cache.GetForUser("u3"));
    }

    [Fact]
    public void GetForUser_snapshot_is_stable_during_concurrent_writes()
    {
        var cache = new AttendanceCache();
        cache.AddAttendance("u1", "evt-1");
        cache.AddAttendance("u1", "evt-2");

        var snapshot = cache.GetForUser("u1");
        var copyBefore = snapshot.ToList();

        cache.AddAttendance("u1", "evt-3");
        cache.RemoveAttendance("u1", "evt-1");

        // The reference returned earlier reflects the cache state at that time —
        // immutable replacement on writes means concurrent enumeration is safe.
        Assert.Equal(copyBefore, snapshot.ToList());
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public void ConcurrentAdd_does_not_lose_attendances()
    {
        var cache = new AttendanceCache();
        var eventIds = Enumerable.Range(1, 200).Select(i => $"evt-{i}").ToList();

        Parallel.ForEach(eventIds, id => cache.AddAttendance("u1", id));

        var stored = cache.GetForUser("u1");
        Assert.Equal(200, stored.Count);
        foreach (var id in eventIds)
            Assert.Contains(id, stored);
    }

    [Fact]
    public void ConcurrentAddRemove_eventually_consistent()
    {
        var cache = new AttendanceCache();
        cache.AddAttendance("u1", "permanent");

        Parallel.For(0, 100, i =>
        {
            cache.AddAttendance("u1", $"temp-{i}");
            cache.RemoveAttendance("u1", $"temp-{i}");
        });

        var stored = cache.GetForUser("u1");
        Assert.Contains("permanent", stored);
        for (int i = 0; i < 100; i++)
            Assert.DoesNotContain($"temp-{i}", stored);
    }

    [Fact]
    public async Task LoadAsync_populates_from_table_grouping_by_user()
    {
        // Three users, mixed across 4 events
        var rows = new List<EventAttendeeEntity>
        {
            MakeRow("evt-1", "u1"),
            MakeRow("evt-1", "u2"),
            MakeRow("evt-2", "u1"),
            MakeRow("evt-3", "u2"),
            MakeRow("evt-3", "u3"),
            MakeRow("evt-4", "u3"),
        };

        var page = Page<EventAttendeeEntity>.FromValues(rows, continuationToken: null, Mock.Of<Response>());
        var pageable = AsyncPageable<EventAttendeeEntity>.FromPages(new[] { page });

        var tc = new Mock<TableClient>();
        tc.Setup(t => t.QueryAsync<EventAttendeeEntity>(
                It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var cache = new AttendanceCache();
        await cache.LoadAsync(tc.Object);

        Assert.Equal(3, cache.UserCount);
        Assert.Equal(new[] { "evt-1", "evt-2" }.OrderBy(x => x),
            cache.GetForUser("u1").OrderBy(x => x));
        Assert.Equal(new[] { "evt-1", "evt-3" }.OrderBy(x => x),
            cache.GetForUser("u2").OrderBy(x => x));
        Assert.Equal(new[] { "evt-3", "evt-4" }.OrderBy(x => x),
            cache.GetForUser("u3").OrderBy(x => x));
    }

    [Fact]
    public async Task LoadAsync_on_empty_table_leaves_cache_empty()
    {
        var page = Page<EventAttendeeEntity>.FromValues(
            new List<EventAttendeeEntity>(), continuationToken: null, Mock.Of<Response>());
        var pageable = AsyncPageable<EventAttendeeEntity>.FromPages(new[] { page });

        var tc = new Mock<TableClient>();
        tc.Setup(t => t.QueryAsync<EventAttendeeEntity>(
                It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var cache = new AttendanceCache();
        await cache.LoadAsync(tc.Object);

        Assert.Equal(0, cache.UserCount);
        Assert.Empty(cache.GetForUser("any-user"));
    }
}
