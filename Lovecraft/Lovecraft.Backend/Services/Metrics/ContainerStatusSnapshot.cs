namespace Lovecraft.Backend.Services.Metrics;

public sealed record ContainerStatusSnapshot(
    string Name,
    DateTime LastHeartbeatUtc,
    DateTime StartedAtUtc,
    string Version,
    long? GcHeapMb,
    long? WorkingSetMb,
    int? ThreadCount,
    double? CpuSecondsTotal,
    double? CpuPercent,
    long? RequestsServed,
    string? Note);
