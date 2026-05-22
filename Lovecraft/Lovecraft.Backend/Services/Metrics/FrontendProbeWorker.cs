using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Services.Metrics;

public sealed class FrontendProbeWorker : BackgroundService
{
    private readonly IMetricsCollector _collector;
    private readonly ILogger<FrontendProbeWorker> _logger;
    private readonly HttpClient _http;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);
    private static readonly string ProbeUrl = Environment.GetEnvironmentVariable("FRONTEND_PROBE_URL") ?? "http://frontend/health";

    public FrontendProbeWorker(IMetricsCollector collector, ILogger<FrontendProbeWorker> logger, IHttpClientFactory httpFactory)
    {
        _collector = collector;
        _logger = logger;
        _http = httpFactory.CreateClient("frontend-probe");
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProbeAsync(stoppingToken);
            await Task.Delay(TickInterval, stoppingToken);
        }
    }

    private async Task ProbeAsync(CancellationToken ct)
    {
        int status = 0;
        try
        {
            var resp = await _http.GetAsync(ProbeUrl, ct);
            status = (int)resp.StatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Frontend probe failed");
        }
        var snap = new ContainerStatusSnapshot(
            Name: "frontend",
            LastHeartbeatUtc: DateTime.UtcNow,
            StartedAtUtc: DateTime.UtcNow,
            Version: "nginx",
            GcHeapMb: null, WorkingSetMb: null, ThreadCount: null, CpuSecondsTotal: null,
            RequestsServed: null,
            Note: $"HTTP {status}");
        try { await _collector.RecordContainerStatusAsync(snap, ct); } catch { }
    }
}
