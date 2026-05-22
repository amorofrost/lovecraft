using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Metrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/metrics")]
[Authorize]
public class MetricsController : ControllerBase
{
    private readonly IMetricsCollector _collector;
    private readonly IAppConfigService _appConfig;

    public MetricsController(IMetricsCollector collector, IAppConfigService appConfig)
    {
        _collector = collector;
        _appConfig = appConfig;
    }

    /// <summary>
    /// Returns the current metrics category toggles (which categories are enabled).
    /// </summary>
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        var cfg = await _appConfig.GetMetricsConfigAsync(ct);
        return Ok(new MetricsConfigDto(cfg.RequestTiming, cfg.BiEvents, cfg.ContainerStats, cfg.FrontendPerf));
    }

    /// <summary>
    /// Accepts a batch of frontend performance samples and forwards each to the metrics collector.
    /// Rate-limited to 10 requests per user per minute.
    /// </summary>
    [HttpPost("frontend")]
    [EnableRateLimiting("MetricsFrontendRateLimit")]
    public Task<IActionResult> PostFrontend([FromBody] FrontendMetricsBatchDto batch)
    {
        if (batch?.Samples is null || batch.Samples.Length == 0)
            return Task.FromResult<IActionResult>(NoContent());

        foreach (var s in batch.Samples)
        {
            var endpoint = NormalizeEndpoint(s.Endpoint);
            var dim = $"frontend|{s.Method}|{endpoint}|{s.Status}";
            _collector.RecordTiming("frontend_perf", dim, s.DurationMs);
        }
        return Task.FromResult<IActionResult>(NoContent());
    }

    /// <summary>
    /// Strips query strings and replaces numeric/GUID path segments with {id} to reduce
    /// cardinality in the metrics store.
    /// </summary>
    public static string NormalizeEndpoint(string raw)
    {
        var q = raw.IndexOf('?');
        var path = q < 0 ? raw : raw[..q];
        var parts = path.Split('/').Select(seg =>
        {
            if (seg.Length == 0) return seg;
            if (long.TryParse(seg, out _)) return "{id}";
            if (Guid.TryParse(seg, out _)) return "{id}";
            return seg;
        });
        return string.Join('/', parts);
    }
}

public sealed record MetricsConfigDto(bool RequestTiming, bool BiEvents, bool ContainerStats, bool FrontendPerf);
public sealed record FrontendMetricsBatchDto(FrontendMetricSampleDto[] Samples);
public sealed record FrontendMetricSampleDto(string Endpoint, string Method, int Status, double DurationMs);
