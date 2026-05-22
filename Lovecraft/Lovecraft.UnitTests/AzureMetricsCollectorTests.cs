using Lovecraft.Backend.Services.Metrics;
using Xunit;

namespace Lovecraft.UnitTests;

public class AzureMetricsCollectorTests
{
    [Fact]
    public void RecordTiming_QueuesSampleWithCapturedTimestamp()
    {
        var c = new AzureMetricsCollector(capacity: 1000);
        c.RecordTiming("request_timing", "backend|GET|/x|200", 42);
        Assert.Equal(1, c.PendingCount);
    }

    [Fact]
    public void Buffer_DropsOldestWhenFull()
    {
        var c = new AzureMetricsCollector(capacity: 2);
        c.RecordTiming("request_timing", "k1", 1);
        c.RecordTiming("request_timing", "k2", 2);
        c.RecordTiming("request_timing", "k3", 3);  // should drop oldest
        Assert.Equal(2, c.PendingCount);
    }

    [Fact]
    public void DrainBatch_GroupsByPkRkAndAggregates()
    {
        var c = new AzureMetricsCollector(capacity: 1000);
        c.RecordTiming("request_timing", "backend|GET|/x|200", 20);
        c.RecordTiming("request_timing", "backend|GET|/x|200", 80);
        c.RecordTiming("request_timing", "backend|GET|/y|200", 50);

        var batch = c.DrainBatchForFlush();
        Assert.Equal(2, batch.Count);
        var xRow = batch.Single(r => r.RowKey.EndsWith("backend|GET|/x|200"));
        Assert.Equal(2, xRow.Count);
        Assert.Equal(100, xRow.SumMs);
    }

    [Fact]
    public void Disabled_DoesNotEnqueue()
    {
        var c = new AzureMetricsCollector(capacity: 1000);
        c.UpdateFlags(MetricsEnabledFlags.AllDisabled);
        c.RecordTiming("request_timing", "k", 10);
        Assert.Equal(0, c.PendingCount);
    }
}
