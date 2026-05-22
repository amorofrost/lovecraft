using Lovecraft.Backend.Services.Metrics;
using Xunit;

namespace Lovecraft.UnitTests;

public class ContainerHeartbeatWorkerTests
{
    [Fact]
    public void Snapshot_PopulatesProcessMetrics()
    {
        var snap = ContainerHeartbeatWorker.CaptureSnapshot("backend", startedAt: DateTime.UtcNow.AddMinutes(-5), version: "1.0");
        Assert.Equal("backend", snap.Name);
        Assert.NotNull(snap.WorkingSetMb);
        Assert.True(snap.WorkingSetMb > 0);
        Assert.NotNull(snap.ThreadCount);
        Assert.True(snap.ThreadCount > 0);
        Assert.NotNull(snap.GcHeapMb);
    }
}
