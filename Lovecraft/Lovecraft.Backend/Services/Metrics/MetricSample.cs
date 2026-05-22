namespace Lovecraft.Backend.Services.Metrics;

public sealed record MetricSample(
    string Category,
    string DimensionKey,
    long Count,
    double? DurationMs,
    DateTime CapturedAtUtc);
