using System.Diagnostics;
using System.Security.Claims;
using Lovecraft.Backend.Services.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lovecraft.Backend.Middleware;

public sealed class RequestMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMetricsCollector _collector;
    private readonly DailyActiveUserCoalescer _dau;

    private static readonly string[] SkippedPathPrefixes =
        { "/health", "/api/v1/metrics/config", "/api/v1/metrics/frontend", "/swagger" };

    public RequestMetricsMiddleware(RequestDelegate next, IMetricsCollector collector, DailyActiveUserCoalescer dau)
    {
        _next = next;
        _collector = collector;
        _dau = dau;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (context.Request.Method == "OPTIONS" ||
            SkippedPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            try
            {
                var routeEndpoint = context.GetEndpoint() as RouteEndpoint;
                var template = routeEndpoint?.RoutePattern.RawText;
                var pathForMetric = MetricsRouteNormalizer.Normalize(
                    !string.IsNullOrEmpty(template) ? template : path);
                var dim = $"backend|{context.Request.Method}|{pathForMetric}|{context.Response.StatusCode}";
                _collector.RecordTiming("request_timing", dim, sw.Elapsed.TotalMilliseconds);

                var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    // Fire-and-forget; FlushIfNeededAsync internally gates via ShouldFlush.
                    _ = _dau.FlushIfNeededAsync(userId, DateTime.UtcNow, context.RequestAborted);
                }
            }
            catch { /* metrics must never fail the request */ }
        }
    }
}
