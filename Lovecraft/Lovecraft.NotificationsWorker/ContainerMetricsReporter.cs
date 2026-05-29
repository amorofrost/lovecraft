using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker;

/// <summary>
/// Best-effort push of this container's gauge samples to the backend's internal
/// metrics ingest endpoint. Never throws. Duplicated from the telegram-bot copy.
/// </summary>
public sealed class ContainerMetricsReporter
{
    private const string Endpoint = "/api/v1/internal/metrics/container-stats";
    private readonly HttpClient _http;
    private readonly string _serviceToken;
    private readonly ILogger<ContainerMetricsReporter> _logger;

    public ContainerMetricsReporter(HttpClient http, string serviceToken, ILogger<ContainerMetricsReporter> logger)
    {
        _http = http;
        _serviceToken = serviceToken;
        _logger = logger;
    }

    public async Task ReportAsync(string container, long? gcHeapMb, long? workingSetMb,
                                  int? threadCount, double? cpuPercent, CancellationToken ct)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            req.Headers.Add("X-Service-Token", _serviceToken);
            req.Content = JsonContent.Create(new { container, gcHeapMb, workingSetMb, threadCount, cpuPercent });
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Container metrics push for {Container} failed: {Status}", container, resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Container metrics push for {Container} threw", container);
        }
    }
}
