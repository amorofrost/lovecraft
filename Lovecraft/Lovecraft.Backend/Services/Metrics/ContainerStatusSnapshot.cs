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
    long? RequestsServed,
    string? Note);
