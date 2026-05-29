namespace Lovecraft.Backend.Services.Metrics;

/// <summary>
/// Derives normalized CPU utilization (0–100% across all cores) from the delta of
/// cumulative processor-seconds between two heartbeat samples. Returns null when a
/// percentage cannot be computed (first sample, non-positive elapsed time, or no cores).
/// </summary>
public static class ContainerCpuMath
{
    public static double? ComputeCpuPercent(double cpuNow, double? cpuPrev, double elapsedSeconds, int processorCount)
    {
        if (cpuPrev is null) return null;
        if (elapsedSeconds <= 0) return null;
        if (processorCount <= 0) return null;

        var pct = (cpuNow - cpuPrev.Value) / (elapsedSeconds * processorCount) * 100.0;
        return Math.Clamp(pct, 0.0, 100.0);
    }
}
