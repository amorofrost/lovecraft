using Lovecraft.Backend.Services.Metrics;
using Xunit;

namespace Lovecraft.UnitTests;

public class MauCalculatorTests
{
    [Fact]
    public void ComputeFromPartitions_DedupsAcrossDays()
    {
        var partitions = new Dictionary<string, string[]>
        {
            ["2026-05-21"] = new[] { "u1", "u2" },
            ["2026-05-20"] = new[] { "u1", "u3" },
            ["2026-05-19"] = new[] { "u4" },
        };
        var mau = MauCalculator.ComputeFromPartitions(partitions);
        Assert.Equal(4, mau);
    }
}
