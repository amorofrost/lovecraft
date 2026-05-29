using Lovecraft.Backend.Services.Metrics;
using Xunit;

namespace Lovecraft.UnitTests;

public class ContainerCpuMathTests
{
    [Fact]
    public void FirstSample_NoPrior_ReturnsNull()
    {
        Assert.Null(ContainerCpuMath.ComputeCpuPercent(cpuNow: 10, cpuPrev: null, elapsedSeconds: 30, processorCount: 4));
    }

    [Fact]
    public void KnownDelta_ComputesNormalizedPercent()
    {
        // 6 cpu-seconds over 30 wall-seconds on 4 cores => 6 / (30*4) * 100 = 5%
        var pct = ContainerCpuMath.ComputeCpuPercent(cpuNow: 16, cpuPrev: 10, elapsedSeconds: 30, processorCount: 4);
        Assert.NotNull(pct);
        Assert.Equal(5.0, pct!.Value, 3);
    }

    [Fact]
    public void OverUsedAllCores_ClampsTo100()
    {
        Assert.Equal(100, ContainerCpuMath.ComputeCpuPercent(0 + 200, 0, 30, 4)!.Value, 3);
    }

    [Fact]
    public void NegativeDelta_ClampsTo0()
    {
        Assert.Equal(0, ContainerCpuMath.ComputeCpuPercent(5, 10, 30, 4)!.Value, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveElapsed_ReturnsNull(double elapsed)
    {
        Assert.Null(ContainerCpuMath.ComputeCpuPercent(16, 10, elapsed, 4));
    }

    [Fact]
    public void ZeroProcessorCount_ReturnsNull()
    {
        Assert.Null(ContainerCpuMath.ComputeCpuPercent(16, 10, 30, 0));
    }
}
