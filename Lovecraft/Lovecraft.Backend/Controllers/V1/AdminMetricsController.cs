using System.Security.Claims;
using Azure.Data.Tables;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Caching;
using Lovecraft.Backend.Services.Metrics;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lovecraft.Backend.Controllers.V1;

// ──────────────────────────────────────────────────────────────────────────────
// DTOs
// ──────────────────────────────────────────────────────────────────────────────

public sealed record MetricsOverviewDto(
    int Registered,
    int Dau,
    int Mau,
    int CurrentlyActive,
    long RequestsLastHour,
    double? P95LastHourMs);

public sealed record ContainerStatusDto(
    string Name,
    string Status,              // "green" | "amber" | "red"
    double? HeartbeatAgeSeconds,
    long? GcHeapMb,
    long? WorkingSetMb,
    int? ThreadCount,
    string? Note,
    DateTime? StartedAtUtc,
    string? Version);

public sealed record TimeseriesPointDto(
    DateTime Ts,
    long Count,
    double? P50,
    double? P95,
    double? P99);

public sealed record BiTimeseriesDto(
    string[] Days,
    int[] Registered,
    int[] Dau,
    int[] Mau);

public sealed record AdminMetricsConfigDto(
    bool RequestTiming,
    bool BiEvents,
    bool ContainerStats,
    bool FrontendPerf,
    int RetentionMinuteHours,
    int RetentionHourDays,
    int RetentionDauDays);

public sealed record EndpointStatDto(
    string RouteKey,            // "{method}|{route}" e.g. "GET|/api/v1/users/{id}"
    string Method,
    string Route,               // "/api/v1/users/{id}"
    long Count,
    double? P50,
    double? P95,
    double? P99);

// ──────────────────────────────────────────────────────────────────────────────
// Controller
// ──────────────────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/v1/admin/metrics")]
[Authorize]
[RequireStaffRole("admin")]
public class AdminMetricsController : ControllerBase
{
    private readonly IAppConfigService _appConfig;
    private readonly UserCache _userCache;
    private readonly MauCalculator _mau;
    private readonly TableServiceClient? _tables;

    public AdminMetricsController(
        IAppConfigService appConfig,
        UserCache userCache,
        MauCalculator mau,
        TableServiceClient? tables = null)
    {
        _appConfig = appConfig;
        _userCache = userCache;
        _mau = mau;
        _tables = tables;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /admin/metrics/config
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("config")]
    public async Task<ActionResult<ApiResponse<AdminMetricsConfigDto>>> GetConfig(CancellationToken ct)
    {
        var cfg = await _appConfig.GetMetricsConfigAsync(ct);
        var dto = ToDto(cfg);
        return Ok(ApiResponse<AdminMetricsConfigDto>.SuccessResponse(dto));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /admin/metrics/config
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("config")]
    public async Task<ActionResult<ApiResponse<AdminMetricsConfigDto>>> PutConfig(
        [FromBody] AdminMetricsConfigDto body,
        CancellationToken ct)
    {
        var newCfg = new MetricsConfig(
            RequestTiming: body.RequestTiming,
            BiEvents: body.BiEvents,
            ContainerStats: body.ContainerStats,
            FrontendPerf: body.FrontendPerf,
            RetentionMinuteHours: body.RetentionMinuteHours,
            RetentionHourDays: body.RetentionHourDays,
            RetentionDauDays: body.RetentionDauDays);

        await _appConfig.SetMetricsConfigAsync(newCfg, ct);
        await _appConfig.InvalidateAsync();

        return Ok(ApiResponse<AdminMetricsConfigDto>.SuccessResponse(ToDto(newCfg)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /admin/metrics/overview
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("overview")]
    public async Task<ActionResult<ApiResponse<MetricsOverviewDto>>> GetOverview(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // registered: count from in-memory user cache (avoids a full table scan)
        var registered = _userCache.GetAll().Count;

        // currently active: users with LastSeen within the last 5 minutes
        var activeThreshold = DateTime.UtcNow.AddMinutes(-5);
        var currentlyActive = _userCache.GetAll()
            .Count(u => u.LastSeen >= activeThreshold);

        // DAU / MAU from the dailyactiveusers table (returns 0 in mock mode)
        var dauTask = _mau.GetDauAsync(today, ct);
        var mauTask = _mau.GetMauAsync(today, ct);
        await Task.WhenAll(dauTask, mauTask);
        var dau = dauTask.Result;
        var mau = mauTask.Result;

        // requestsLastHour + P95 from metricsminute table
        var (requestsLastHour, p95) = await GetLastHourRequestStatsAsync(ct);

        var dto = new MetricsOverviewDto(
            Registered: registered,
            Dau: dau,
            Mau: mau,
            CurrentlyActive: currentlyActive,
            RequestsLastHour: requestsLastHour,
            P95LastHourMs: p95);

        return Ok(ApiResponse<MetricsOverviewDto>.SuccessResponse(dto));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /admin/metrics/containers
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("containers")]
    public async Task<ActionResult<ApiResponse<List<ContainerStatusDto>>>> GetContainers(CancellationToken ct)
    {
        if (_tables is null)
            return Ok(ApiResponse<List<ContainerStatusDto>>.SuccessResponse(new List<ContainerStatusDto>()));

        var table = _tables.GetTableClient(TableNames.ContainerStatus);
        await table.CreateIfNotExistsAsync(ct);

        var now = DateTime.UtcNow;
        var results = new List<ContainerStatusDto>();

        await foreach (var entity in table.QueryAsync<ContainerStatusEntity>(
            filter: $"PartitionKey eq 'STATUS'", cancellationToken: ct))
        {
            var ageSeconds = (now - entity.LastHeartbeatUtc).TotalSeconds;
            string status;

            // Frontend probe rows have a Note like "HTTP 200 OK" or "HTTP 5xx …"
            // Treat them differently: green if 2xx, red otherwise.
            if (entity.Note is not null && entity.Note.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = entity.Note.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var httpStatus))
                    status = httpStatus is >= 200 and < 300 ? "green" : "red";
                else
                    status = ageSeconds < 60 ? "green" : ageSeconds < 180 ? "amber" : "red";
            }
            else
            {
                status = ageSeconds < 60 ? "green" : ageSeconds < 180 ? "amber" : "red";
            }

            results.Add(new ContainerStatusDto(
                Name: entity.RowKey,
                Status: status,
                HeartbeatAgeSeconds: ageSeconds,
                GcHeapMb: entity.GcHeapMb,
                WorkingSetMb: entity.WorkingSetMb,
                ThreadCount: entity.ThreadCount,
                Note: entity.Note,
                StartedAtUtc: entity.StartedAtUtc == default ? null : entity.StartedAtUtc,
                Version: string.IsNullOrEmpty(entity.Version) ? null : entity.Version));
        }

        return Ok(ApiResponse<List<ContainerStatusDto>>.SuccessResponse(results));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /admin/metrics/timeseries
    // Query params: category, dimensionKey?, from (ISO), to (ISO), resolution=minute|hour
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("timeseries")]
    public async Task<ActionResult<ApiResponse<List<TimeseriesPointDto>>>> GetTimeseries(
        [FromQuery] string category,
        [FromQuery] string? dimensionKey,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string resolution = "minute",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            return BadRequest(ApiResponse<List<TimeseriesPointDto>>.ErrorResponse("MISSING_PARAM", "category is required"));

        if (_tables is null)
            return Ok(ApiResponse<List<TimeseriesPointDto>>.SuccessResponse(new List<TimeseriesPointDto>()));

        var useMinute = !string.Equals(resolution, "hour", StringComparison.OrdinalIgnoreCase);
        var tableName = useMinute ? TableNames.MetricsMinute : TableNames.MetricsHour;
        var table = _tables.GetTableClient(tableName);
        await table.CreateIfNotExistsAsync(ct);

        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to, DateTimeKind.Utc);

        List<TimeseriesPointDto> points;
        if (useMinute)
            points = await QueryMinuteTimeseriesAsync(table, category, dimensionKey, null, fromUtc, toUtc, ct);
        else
            points = await QueryHourTimeseriesAsync(table, category, dimensionKey, null, fromUtc, toUtc, ct);

        return Ok(ApiResponse<List<TimeseriesPointDto>>.SuccessResponse(points));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /admin/metrics/bi
    // Query params: range=24h|7d|30d
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("bi")]
    public async Task<ActionResult<ApiResponse<BiTimeseriesDto>>> GetBi(
        [FromQuery] string range = "7d",
        CancellationToken ct = default)
    {
        var days = range switch
        {
            "24h" => 1,
            "30d" => 30,
            _ => 7,      // default: 7d
        };

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var allUsers = _userCache.GetAll();

        var dayLabels = new string[days];
        var registered = new int[days];
        var dau = new int[days];
        var mau = new int[days];

        var dauTasks = new Task<int>[days];
        var mauTasks = new Task<int>[days];

        for (int i = 0; i < days; i++)
        {
            var day = today.AddDays(-i);
            dayLabels[i] = day.ToString("yyyy-MM-dd");

            // Registered: count of users whose CreatedAt falls on this day
            var dayStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var dayEnd = dayStart.AddDays(1);
            registered[i] = allUsers.Count(u => u.CreatedAt >= dayStart && u.CreatedAt < dayEnd);

            dauTasks[i] = _mau.GetDauAsync(day, ct);
            mauTasks[i] = _mau.GetMauAsync(day, ct);
        }

        await Task.WhenAll(dauTasks.Concat(mauTasks));

        for (int i = 0; i < days; i++)
        {
            dau[i] = dauTasks[i].Result;
            mau[i] = mauTasks[i].Result;
        }

        // Return in chronological order (oldest first)
        Array.Reverse(dayLabels);
        Array.Reverse(registered);
        Array.Reverse(dau);
        Array.Reverse(mau);

        return Ok(ApiResponse<BiTimeseriesDto>.SuccessResponse(
            new BiTimeseriesDto(dayLabels, registered, dau, mau)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /admin/metrics/endpoint-stats
    // Query params: from (ISO), to (ISO), resolution=minute|hour
    // Returns per-endpoint totals aggregated across the time range and ALL status
    // codes, sorted by count desc.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("endpoint-stats")]
    public async Task<ActionResult<ApiResponse<List<EndpointStatDto>>>> GetEndpointStats(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string resolution = "minute",
        CancellationToken ct = default)
    {
        if (_tables is null)
            return Ok(ApiResponse<List<EndpointStatDto>>.SuccessResponse(new List<EndpointStatDto>()));

        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc   = DateTime.SpecifyKind(to,   DateTimeKind.Utc);
        var useMinute = !string.Equals(resolution, "hour", StringComparison.OrdinalIgnoreCase);
        var table = _tables.GetTableClient(useMinute ? TableNames.MetricsMinute : TableNames.MetricsHour);
        await table.CreateIfNotExistsAsync(ct);

        var rows = new List<(string dimensionKey, long count, long[] buckets)>();

        if (useMinute)
        {
            var cursor = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, fromUtc.Hour, 0, 0, DateTimeKind.Utc);
            while (cursor <= toUtc)
            {
                var pk = $"{cursor:yyyy-MM-dd'T'HH}_request_timing";
                await foreach (var entity in table.QueryAsync<MetricMinuteEntity>(
                    filter: $"PartitionKey eq '{pk}'", cancellationToken: ct))
                {
                    var rk = entity.RowKey.Split('_', 2);
                    if (rk.Length < 2 || !int.TryParse(rk[0], out var min)) continue;
                    var ts = new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, min, 0, DateTimeKind.Utc);
                    if (ts < fromUtc || ts > toUtc) continue;
                    rows.Add((rk[1], entity.Count, BucketsOf(entity.B0, entity.B1, entity.B2, entity.B3,
                        entity.B4, entity.B5, entity.B6, entity.B7, entity.B8)));
                }
                cursor = cursor.AddHours(1);
            }
        }
        else
        {
            var cursor = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, 0, 0, 0, DateTimeKind.Utc);
            while (cursor <= toUtc)
            {
                var pk = $"{cursor:yyyy-MM-dd}_request_timing";
                await foreach (var entity in table.QueryAsync<MetricHourEntity>(
                    filter: $"PartitionKey eq '{pk}'", cancellationToken: ct))
                {
                    var rk = entity.RowKey.Split('_', 2);
                    if (rk.Length < 2 || !int.TryParse(rk[0], out var hr)) continue;
                    var ts = new DateTime(cursor.Year, cursor.Month, cursor.Day, hr, 0, 0, DateTimeKind.Utc);
                    if (ts < fromUtc || ts > toUtc) continue;
                    rows.Add((rk[1], entity.Count, BucketsOf(entity.B0, entity.B1, entity.B2, entity.B3,
                        entity.B4, entity.B5, entity.B6, entity.B7, entity.B8)));
                }
                cursor = cursor.AddDays(1);
            }
        }

        return Ok(ApiResponse<List<EndpointStatDto>>.SuccessResponse(AggregateEndpointStats(rows)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /admin/metrics/endpoint-timeseries
    // Query params: method, route, from (ISO), to (ISO), resolution=minute|hour
    // Returns count + percentile timeseries for one endpoint, summed across statuses.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("endpoint-timeseries")]
    public async Task<ActionResult<ApiResponse<List<TimeseriesPointDto>>>> GetEndpointTimeseries(
        [FromQuery] string method,
        [FromQuery] string route,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string resolution = "minute",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(route))
            return BadRequest(ApiResponse<List<TimeseriesPointDto>>.ErrorResponse(
                "MISSING_PARAM", "method and route are required"));

        if (_tables is null)
            return Ok(ApiResponse<List<TimeseriesPointDto>>.SuccessResponse(new List<TimeseriesPointDto>()));

        var prefix = BuildEndpointDimPrefix(method, route);
        var useMinute = !string.Equals(resolution, "hour", StringComparison.OrdinalIgnoreCase);
        var table = _tables.GetTableClient(useMinute ? TableNames.MetricsMinute : TableNames.MetricsHour);
        await table.CreateIfNotExistsAsync(ct);

        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc   = DateTime.SpecifyKind(to,   DateTimeKind.Utc);

        var points = useMinute
            ? await QueryMinuteTimeseriesAsync(table, "request_timing", null, prefix, fromUtc, toUtc, ct)
            : await QueryHourTimeseriesAsync(table, "request_timing", null, prefix, fromUtc, toUtc, ct);

        return Ok(ApiResponse<List<TimeseriesPointDto>>.SuccessResponse(points));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Percentile interpolation helper
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Linearly interpolates a percentile from histogram bucket counts.
    /// <paramref name="pct"/> is 0–100. Returns null if there are no samples.
    /// </summary>
    public static double? InterpolatePercentile(long[] buckets, double[] boundaries, double pct)
    {
        var total = buckets.Sum();
        if (total == 0) return null;
        var target = total * pct / 100.0;
        long cum = 0;
        for (int i = 0; i < buckets.Length; i++)
        {
            cum += buckets[i];
            if (cum >= target)
            {
                var lower = i == 0 ? 0 : boundaries[i - 1];
                var upper = i < boundaries.Length ? boundaries[i] : boundaries[^1] * 2;
                var bucketCount = buckets[i];
                var positionInBucket = bucketCount == 0 ? 0 : (target - (cum - bucketCount)) / bucketCount;
                return lower + (upper - lower) * positionInBucket;
            }
        }
        return boundaries[^1];
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static AdminMetricsConfigDto ToDto(MetricsConfig cfg) => new(
        cfg.RequestTiming,
        cfg.BiEvents,
        cfg.ContainerStats,
        cfg.FrontendPerf,
        cfg.RetentionMinuteHours,
        cfg.RetentionHourDays,
        cfg.RetentionDauDays);

    private async Task<(long requests, double? p95)> GetLastHourRequestStatsAsync(CancellationToken ct)
    {
        if (_tables is null) return (0, null);

        var table = _tables.GetTableClient(TableNames.MetricsMinute);
        await table.CreateIfNotExistsAsync(ct);

        var now = DateTime.UtcNow;
        long totalRequests = 0;
        long[] combinedBuckets = new long[HistogramBuckets.BucketCount];

        // Partition key format: "{yyyy-MM-dd'T'HH}_{category}"
        // We need to scan the last 60 minutes, which may span up to 2 hour partitions.
        for (int h = 0; h <= 1; h++)
        {
            var hour = now.AddHours(-h);
            var pk = $"{hour:yyyy-MM-dd'T'HH}_request_timing";

            await foreach (var entity in table.QueryAsync<MetricMinuteEntity>(
                filter: $"PartitionKey eq '{pk}'", cancellationToken: ct))
            {
                // Row key: "{mm}_{dimensionKey}" — parse minute from first segment
                var mmStr = entity.RowKey.Split('_')[0];
                if (!int.TryParse(mmStr, out var minute)) continue;

                // Reconstruct the timestamp for this row
                var rowTime = new DateTime(hour.Year, hour.Month, hour.Day, hour.Hour, minute, 0, DateTimeKind.Utc);
                if (rowTime < now.AddHours(-1)) continue;  // outside last-hour window

                totalRequests += entity.Count;
                combinedBuckets[0] += entity.B0 ?? 0;
                combinedBuckets[1] += entity.B1 ?? 0;
                combinedBuckets[2] += entity.B2 ?? 0;
                combinedBuckets[3] += entity.B3 ?? 0;
                combinedBuckets[4] += entity.B4 ?? 0;
                combinedBuckets[5] += entity.B5 ?? 0;
                combinedBuckets[6] += entity.B6 ?? 0;
                combinedBuckets[7] += entity.B7 ?? 0;
                combinedBuckets[8] += entity.B8 ?? 0;
            }
        }

        var p95 = InterpolatePercentile(combinedBuckets, HistogramBuckets.Boundaries, 95);
        return (totalRequests, p95);
    }

    private async Task<List<TimeseriesPointDto>> QueryMinuteTimeseriesAsync(
        TableClient table,
        string category,
        string? dimensionKey,
        string? dimensionKeyPrefix,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        // Partition key: "{yyyy-MM-ddTHH}_{category}"
        // We collect all relevant hour partitions in the [from, to] range.
        var byMinute = new Dictionary<DateTime, (long count, long[] buckets)>();

        var cursor = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0, DateTimeKind.Utc);
        while (cursor <= to)
        {
            var pk = $"{cursor:yyyy-MM-dd'T'HH}_{category}";
            await foreach (var entity in table.QueryAsync<MetricMinuteEntity>(
                filter: $"PartitionKey eq '{pk}'", cancellationToken: ct))
            {
                var parts = entity.RowKey.Split('_', 2);
                if (parts.Length < 2) continue;
                var mmStr = parts[0];
                var dimKey = parts[1];

                if (dimensionKey is not null && !string.Equals(dimKey, dimensionKey, StringComparison.Ordinal))
                    continue;
                if (dimensionKeyPrefix is not null && !dimKey.StartsWith(dimensionKeyPrefix, StringComparison.Ordinal))
                    continue;

                if (!int.TryParse(mmStr, out var minute)) continue;
                var ts = new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, minute, 0, DateTimeKind.Utc);
                if (ts < from || ts > to) continue;

                if (!byMinute.TryGetValue(ts, out var acc))
                    acc = (0, new long[HistogramBuckets.BucketCount]);

                acc.count += entity.Count;
                acc.buckets[0] += entity.B0 ?? 0;
                acc.buckets[1] += entity.B1 ?? 0;
                acc.buckets[2] += entity.B2 ?? 0;
                acc.buckets[3] += entity.B3 ?? 0;
                acc.buckets[4] += entity.B4 ?? 0;
                acc.buckets[5] += entity.B5 ?? 0;
                acc.buckets[6] += entity.B6 ?? 0;
                acc.buckets[7] += entity.B7 ?? 0;
                acc.buckets[8] += entity.B8 ?? 0;
                byMinute[ts] = acc;
            }
            cursor = cursor.AddHours(1);
        }

        return byMinute
            .OrderBy(kv => kv.Key)
            .Select(kv => new TimeseriesPointDto(
                Ts: kv.Key,
                Count: kv.Value.count,
                P50: InterpolatePercentile(kv.Value.buckets, HistogramBuckets.Boundaries, 50),
                P95: InterpolatePercentile(kv.Value.buckets, HistogramBuckets.Boundaries, 95),
                P99: InterpolatePercentile(kv.Value.buckets, HistogramBuckets.Boundaries, 99)))
            .ToList();
    }

    private async Task<List<TimeseriesPointDto>> QueryHourTimeseriesAsync(
        TableClient table,
        string category,
        string? dimensionKey,
        string? dimensionKeyPrefix,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        // Partition key: "{yyyy-MM-dd}_{category}"
        var byHour = new Dictionary<DateTime, (long count, long[] buckets)>();

        var cursor = new DateTime(from.Year, from.Month, from.Day, 0, 0, 0, DateTimeKind.Utc);
        while (cursor <= to)
        {
            var pk = $"{cursor:yyyy-MM-dd}_{category}";
            await foreach (var entity in table.QueryAsync<MetricHourEntity>(
                filter: $"PartitionKey eq '{pk}'", cancellationToken: ct))
            {
                var parts = entity.RowKey.Split('_', 2);
                if (parts.Length < 2) continue;
                var hhStr = parts[0];
                var dimKey = parts[1];

                if (dimensionKey is not null && !string.Equals(dimKey, dimensionKey, StringComparison.Ordinal))
                    continue;
                if (dimensionKeyPrefix is not null && !dimKey.StartsWith(dimensionKeyPrefix, StringComparison.Ordinal))
                    continue;

                if (!int.TryParse(hhStr, out var hour)) continue;
                var ts = new DateTime(cursor.Year, cursor.Month, cursor.Day, hour, 0, 0, DateTimeKind.Utc);
                if (ts < from || ts > to) continue;

                if (!byHour.TryGetValue(ts, out var acc))
                    acc = (0, new long[HistogramBuckets.BucketCount]);

                acc.count += entity.Count;
                acc.buckets[0] += entity.B0 ?? 0;
                acc.buckets[1] += entity.B1 ?? 0;
                acc.buckets[2] += entity.B2 ?? 0;
                acc.buckets[3] += entity.B3 ?? 0;
                acc.buckets[4] += entity.B4 ?? 0;
                acc.buckets[5] += entity.B5 ?? 0;
                acc.buckets[6] += entity.B6 ?? 0;
                acc.buckets[7] += entity.B7 ?? 0;
                acc.buckets[8] += entity.B8 ?? 0;
                byHour[ts] = acc;
            }
            cursor = cursor.AddDays(1);
        }

        return byHour
            .OrderBy(kv => kv.Key)
            .Select(kv => new TimeseriesPointDto(
                Ts: kv.Key,
                Count: kv.Value.count,
                P50: InterpolatePercentile(kv.Value.buckets, HistogramBuckets.Boundaries, 50),
                P95: InterpolatePercentile(kv.Value.buckets, HistogramBuckets.Boundaries, 95),
                P99: InterpolatePercentile(kv.Value.buckets, HistogramBuckets.Boundaries, 99)))
            .ToList();
    }

    private static string StripStatus(string dimKey)
    {
        var lastPipe = dimKey.LastIndexOf('|');
        return lastPipe > 0 ? dimKey[..lastPipe] : dimKey;
    }

    public static string BuildEndpointDimPrefix(string method, string route)
    {
        var encoded = route.TrimStart('/').Replace('/', '~');
        return $"backend|{method}|{encoded}|";
    }

    /// <summary>
    /// Groups raw metric rows by (method, route) — dropping the status-code suffix —
    /// summing counts and merging histogram buckets. Sorted by count descending.
    /// </summary>
    public static List<EndpointStatDto> AggregateEndpointStats(
        IEnumerable<(string dimensionKey, long count, long[] buckets)> rows)
    {
        var byRoute = new Dictionary<string, (long count, long[] buckets)>();
        foreach (var (dimKey, count, buckets) in rows)
        {
            var routeDim = StripStatus(dimKey);
            if (!byRoute.TryGetValue(routeDim, out var acc))
                acc = (0, new long[HistogramBuckets.BucketCount]);
            acc.count += count;
            for (int i = 0; i < HistogramBuckets.BucketCount; i++)
                acc.buckets[i] += buckets[i];
            byRoute[routeDim] = acc;
        }

        return byRoute
            .Select(kv => ParseEndpointStat(kv.Key, kv.Value.count, kv.Value.buckets))
            .OrderByDescending(x => x.Count)
            .ToList();
    }

    private static EndpointStatDto ParseEndpointStat(string routeDim, long count, long[] buckets)
    {
        // routeDim format: "backend|METHOD|route~path"  (status already stripped)
        var parts = routeDim.Split('|');
        var method = parts.Length > 1 ? parts[1] : "?";
        var route  = parts.Length > 2 ? "/" + parts[2].Replace('~', '/') : routeDim;

        return new EndpointStatDto(
            RouteKey: $"{method}|{route}",
            Method:   method,
            Route:    route,
            Count:    count,
            P50: InterpolatePercentile(buckets, HistogramBuckets.Boundaries, 50),
            P95: InterpolatePercentile(buckets, HistogramBuckets.Boundaries, 95),
            P99: InterpolatePercentile(buckets, HistogramBuckets.Boundaries, 99));
    }

    private static long[] BucketsOf(long? b0, long? b1, long? b2, long? b3, long? b4,
                                    long? b5, long? b6, long? b7, long? b8) =>
        new[] { b0 ?? 0, b1 ?? 0, b2 ?? 0, b3 ?? 0, b4 ?? 0, b5 ?? 0, b6 ?? 0, b7 ?? 0, b8 ?? 0 };
}
