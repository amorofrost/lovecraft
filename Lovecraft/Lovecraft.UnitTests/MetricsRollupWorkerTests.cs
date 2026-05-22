using Lovecraft.NotificationsWorker.Entities;
using Lovecraft.NotificationsWorker.Workers;

namespace Lovecraft.UnitTests;

public class MetricsRollupWorkerTests
{
    [Fact]
    public void Aggregate_SumsCountAndBuckets()
    {
        var rows = new[]
        {
            new MetricMinuteEntity { RowKey = "10_k", Count = 3, SumMs = 60, MinMs = 10, MaxMs = 30, B0 = 1, B1 = 2 },
            new MetricMinuteEntity { RowKey = "11_k", Count = 2, SumMs = 200, MinMs = 90, MaxMs = 110, B1 = 1, B2 = 1 },
        };
        var hour = MetricsRollupWorker.AggregateGroup("k", rows);
        Assert.Equal(5, hour.Count);
        Assert.Equal(260, hour.SumMs);
        Assert.Equal(10, hour.MinMs);
        Assert.Equal(110, hour.MaxMs);
        Assert.Equal(1, hour.B0);
        Assert.Equal(3, hour.B1);
        Assert.Equal(1, hour.B2);
        Assert.Equal(2, hour.SourceMinuteRowCount);
    }
}
