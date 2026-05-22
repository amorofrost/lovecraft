using Lovecraft.Backend.Services.Metrics;
using Xunit;

namespace Lovecraft.UnitTests;

public class MockMetricsCollectorTests
{
    [Fact]
    public void RecordTiming_Disabled_DoesNotStore()
    {
        var c = new MockMetricsCollector();
        c.UpdateFlags(MetricsEnabledFlags.AllDisabled);
        c.RecordTiming("request_timing", "backend|GET|/x|200", 42);
        Assert.Empty(c.Snapshot());
    }

    [Fact]
    public void RecordTiming_Enabled_MergesIntoSameBucket()
    {
        var c = new MockMetricsCollector();
        c.RecordTiming("request_timing", "backend|GET|/x|200", 20);
        c.RecordTiming("request_timing", "backend|GET|/x|200", 80);
        var rows = c.Snapshot();
        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(2, row.Count);
        Assert.Equal(100, row.SumMs);
        Assert.Equal(20, row.MinMs);
        Assert.Equal(80, row.MaxMs);
        Assert.Equal(1, row.Buckets[0]);  // 20 <= 25 (bucket 0)
        Assert.Equal(1, row.Buckets[2]);  // 80 <= 100 (bucket 2)
    }

    [Fact]
    public void RecordCount_Disabled_DoesNotStore()
    {
        var c = new MockMetricsCollector();
        c.UpdateFlags(new MetricsEnabledFlags(BiEvents: false));
        c.RecordCount("bi_events", "bi|user_registered|local");
        Assert.Empty(c.Snapshot());
    }

    [Fact]
    public void RecordCount_Enabled_Accumulates()
    {
        var c = new MockMetricsCollector();
        c.RecordCount("bi_events", "bi|user_registered|local");
        c.RecordCount("bi_events", "bi|user_registered|local", 4);
        var rows = c.Snapshot();
        Assert.Single(rows);
        Assert.Equal(5, rows[0].Count);
    }

    [Fact]
    public async Task RecordContainerStatus_StoresLatest()
    {
        var c = new MockMetricsCollector();
        var snap = new ContainerStatusSnapshot("backend", DateTime.UtcNow, DateTime.UtcNow, "1.0", 100, 200, 8, 1.5, 42, null);
        await c.RecordContainerStatusAsync(snap);
        var status = c.GetContainerStatus("backend");
        Assert.NotNull(status);
        Assert.Equal("backend", status!.Name);
    }
}
