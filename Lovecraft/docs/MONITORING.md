# Monitoring & Instrumentation — Backend

**Status:** Shipped 2026-05-22.

**Full operator guide:** [`aloevera-harmony-meet/docs/MONITORING.md`](../../../aloevera-harmony-meet/docs/MONITORING.md) — this doc covers backend-specific operational notes only.

**Design spec:** [`aloevera-harmony-meet/docs/superpowers/specs/2026-05-21-monitoring-design.md`](../../../aloevera-harmony-meet/docs/superpowers/specs/2026-05-21-monitoring-design.md)
**Implementation plan:** [`aloevera-harmony-meet/docs/superpowers/plans/2026-05-21-monitoring.md`](../../../aloevera-harmony-meet/docs/superpowers/plans/2026-05-21-monitoring.md)
**Storage schema:** [AZURE_STORAGE.md](./AZURE_STORAGE.md#monitoring--metrics)

---

## What this adds to the backend

- `IMetricsCollector` singleton (Azure / Mock / NoOp variants) — buffer + flush pattern.
- `RequestMetricsMiddleware` capturing every HTTP request after auth, timed via `Stopwatch`, dimension key `backend|{method}|{route}|{status}` (route template, not raw URL).
- 14 BI producer call sites (user_registered, user_login, message_sent, match_created, event_registered, topic_created).
- 4 new BackgroundServices in `Lovecraft.Backend`: `MetricsFlushWorker` (10s drain), `MetricsConfigPoller` (60s toggle refresh), `ContainerHeartbeatWorker` (30s snapshot), `FrontendProbeWorker` (60s HTTP probe of frontend `/health`).
- 1 new BackgroundService in `Lovecraft.NotificationsWorker`: `MetricsRollupWorker` (hourly at `:05`, 6h lookback).
- Extended `JanitorWorker` (NotificationsWorker) with 3 retention passes for the new tables.
- Duplicated heartbeat worker in `Lovecraft.TelegramBot` and `Lovecraft.NotificationsWorker` (no shared lib — matches the existing entity-duplication precedent for worker projects).
- `MauCalculator` (5-min `IMemoryCache`) computing DAU/MAU from `dailyactiveusers`.
- `MetricsController` (public endpoints `/api/v1/metrics/config`, `/api/v1/metrics/frontend`) + `AdminMetricsController` (`/api/v1/admin/metrics/{overview,containers,timeseries,bi,config}`).
- **Serilog** structured JSON to stdout in all 3 .NET containers — enrichers `service`, `version`, `traceId` (auto from `Activity`). Backend also has `UseSerilogRequestLogging()` for request summary lines. `X-Request-Id` echoed on responses.

---

## Configuration

### appconfig partition `metrics` (7 rows)

| RK | Default | Type |
|---|---|---|
| `request_timing` | `true` | bool |
| `bi_events` | `true` | bool |
| `container_stats` | `true` | bool |
| `frontend_perf` | `true` | bool |
| `retention_minute_hours` | `24` | int |
| `retention_hour_days` | `90` | int |
| `retention_dau_days` | `30` | int |

Seeded by `Lovecraft.Tools.Seeder` on initial run. Missing rows fall back to `MetricsConfig.Defaults` (same values).

Toggles refresh into the running collector every 60s via `MetricsConfigPoller`. The admin `PUT /api/v1/admin/metrics/config` also calls `IAppConfigService.InvalidateAsync()` so changes are visible within seconds, not minutes.

### Environment variables

No new env vars introduced — the metrics pipeline reuses the existing `USE_AZURE_STORAGE` + `AZURE_STORAGE_CONNECTION_STRING` connection. `FRONTEND_PROBE_URL` is optional (defaults to `http://frontend/health` for the Docker internal network).

### DI mode-switching (`Program.cs`)

```csharp
if (useAzureStorage)
{
    builder.Services.AddSingleton<IMetricsCollector>(sp =>
        new AzureMetricsCollector(capacity: 1000,
            tableService: sp.GetRequiredService<TableServiceClient>()));
    builder.Services.AddSingleton(sp => new DailyActiveUserCoalescer(60,
        sp.GetRequiredService<TableServiceClient>()));
}
else
{
    builder.Services.AddSingleton<IMetricsCollector, MockMetricsCollector>();
    builder.Services.AddSingleton(new DailyActiveUserCoalescer(60));
}
builder.Services.AddHostedService<MetricsFlushWorker>();
builder.Services.AddHostedService<MetricsConfigPoller>();
builder.Services.AddHostedService(sp => new ContainerHeartbeatWorker(
    sp.GetRequiredService<IMetricsCollector>(), sp.GetRequiredService<ILogger<ContainerHeartbeatWorker>>(),
    "backend", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"));
builder.Services.AddHttpClient("frontend-probe");
builder.Services.AddHostedService<FrontendProbeWorker>();
// ...
app.UseAuthorization();
app.UseMiddleware<RequestMetricsMiddleware>();
```

In mock mode (`USE_AZURE_STORAGE=false`), the collector buffers samples in memory only — the dashboard renders but with zero historical data and resets on restart.

---

## Critical lesson learned: Azure PK/RK constraints

Azure Table Storage **forbids** the following characters in PartitionKey and RowKey:

- `/` (forward slash)
- `\` (backslash)
- `#` (number sign)
- `?` (question mark)
- Control chars (U+0000–U+001F, U+007F–U+009F)

Our original design used `#` as a separator (`{yyyy-MM-ddTHH}#{category}`) and embedded raw URL paths with `/` chars into the RowKey via dimension keys. Every write to `metricsminute` silently failed with:

```
Azure.RequestFailedException: The 'PartitionKey' parameter of value
  '2026-05-22T19#container_stats' is out of range.
```

Container heartbeats worked (no `#` in their keys — `PK="STATUS"`, `RK=containerName`). DAU writes worked (PK is just `yyyy-MM-dd`). So the dashboard's KPI tiles and container grid populated correctly, masking the bug — until logs were grepped.

**Fix shipped in `dd4c13d`:** `#` → `_` as the separator (date components have no `_`, so first `_` is unambiguous), `/` → `~` in URL paths within dimension keys.

**Going forward:** never put raw user-controllable strings (URL paths, opaque event IDs, etc.) directly into PK/RK. Always run them through a sanitizer. The unit tests using `MockMetricsCollector` (in-memory `ConcurrentDictionary`) accept any keys — this class of bug is invisible until real Azure Storage rejects the write. Consider adding integration tests with Azurite if you extend the metrics surface.

**Route normalization (2026-05-27):** `MetricsRouteNormalizer` (`Services/Metrics/`) is the single source for collapsing request paths and route templates into Azure-safe dimension segments — GUID/integer segments and `{id:constraint}` templates become `{id}`/`{name}`, joined with `~`. `RequestMetricsMiddleware` now reads the matched `RouteEndpoint.RoutePattern.RawText` (the previous `Metadata.GetMetadata<RouteEndpoint>()` call always returned null, silently falling back to the raw GUID path). `MetricsController` reuses the same helper for `frontend_perf` ingest so both sources share one shape.

---

## Endpoints

All under `/api/v1/`. Admin endpoints are gated `[RequireStaffRole("admin")]`.

| Method | Path | Auth | Use |
|---|---|---|---|
| `GET` | `/metrics/config` | `[Authorize]` | Returns `{ requestTiming, biEvents, containerStats, frontendPerf }` — frontend interceptor polls every 5min |
| `POST` | `/metrics/frontend` | `[Authorize]` + per-user `MetricsFrontendRateLimit` (10/min) | Browser batch ingest |
| `GET` | `/admin/metrics/overview` | admin | KPI tiles |
| `GET` | `/admin/metrics/containers` | admin | Status grid with green/amber/red |
| `GET` | `/admin/metrics/timeseries` | admin | `?category=&dimensionKey?=&from=&to=&resolution=minute\|hour` |
| `GET` | `/admin/metrics/endpoint-timeseries` | admin | Per-endpoint count+latency, summed across statuses (`?method=&route=&from=&to=&resolution=`) |
| `GET` | `/admin/metrics/bi` | admin | `?range=24h\|7d\|30d` |
| `GET` | `/admin/metrics/config` | admin | Read all 7 metrics appconfig rows |
| `PUT` | `/admin/metrics/config` | admin | Write + invalidate cache |

Rate limit policy `MetricsFrontendRateLimit` is registered alongside `AuthRateLimit` in `Program.cs`. Partition key is the authenticated user ID (falls back to IP if claim missing).

---

## Skipped paths in `RequestMetricsMiddleware`

The middleware skip-list:

```csharp
{ "/health", "/api/v1/metrics/config", "/api/v1/metrics/frontend", "/swagger" }
```

Plus `OPTIONS` requests (CORS preflight). Without these skips, the metrics endpoints themselves would generate samples, creating a feedback loop where the frontend's `POST /api/v1/metrics/frontend` flush call gets recorded and shipped back in the next flush.

(The initial implementation had a bug where the skip-list used `/metrics/config` instead of `/api/v1/metrics/config`; caught in final review and fixed in `fb2d8e2`.)

---

## Logs

Serilog config in each `Program.cs`:

```csharp
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "backend")  // or "telegram-bot" / "notifications-worker"
    .Enrich.WithProperty("version", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0")
    .WriteTo.Console(new RenderedCompactJsonFormatter()));
```

`appsettings.json` adds overrides to silence ASP.NET pipeline noise:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "Azure.Core": "Warning"
      }
    }
  }
}
```

### Reading logs in production

```bash
docker compose logs backend 2>&1 | head -20                       # latest lines
docker compose logs backend 2>&1 | grep '"@l":"Error"'            # errors only
docker compose logs backend 2>&1 | grep '"traceId":"00-abc"'      # one request
docker compose logs notifications-worker 2>&1 | grep '"Metrics'   # rollup/flush messages
```

`docker compose` default log rotation: 10 MB × 3 files per container. If logs become too noisy, adjust `logging.options.max-size` / `max-file` in `docker-compose.yml`.

---

## What's not in this PR (deferred)

- **Log shipping.** Stdout JSON only; add `cfg.WriteTo.ApplicationInsights(...)` / `Seq(...)` / `Loki(...)` when a sink is chosen.
- **Application Insights distributed tracing** (`TD.5` originally proposed this).
- **Alerting** (threshold-based push to Slack / email).
- **Per-endpoint sampling rates** (per-category is the only granularity today).
- **Web Vitals + navigation timing** on the frontend (apiClient call latency only).
- **Admin shell's frontend init.** `src/admin/main.tsx` doesn't call `frontendMetrics.init()` — admin-page API traffic doesn't appear in `frontend_perf`. One-line fix.

---

## Where to look in code

```
Lovecraft.Backend/
  Services/Metrics/
    IMetricsCollector.cs, MetricSample.cs, MetricsEnabledFlags.cs,
    ContainerStatusSnapshot.cs, HistogramBuckets.cs
    AzureMetricsCollector.cs    — channel buffer + 10s flush + merge writes
    MockMetricsCollector.cs     — in-memory for tests + mock mode
    NoOpMetricsCollector.cs     — for fully-disabled deploys
    MetricsFlushWorker.cs       — BackgroundService 10s tick
    MetricsConfigPoller.cs      — BackgroundService 60s tick
    ContainerHeartbeatWorker.cs — BackgroundService 30s tick (backend only;
                                   telegram-bot + notifications-worker have their own copies)
    FrontendProbeWorker.cs      — BackgroundService 60s tick HTTPing /health
    DailyActiveUserCoalescer.cs — coalesced DAU upserter
    MauCalculator.cs            — 30-partition union with IMemoryCache
  Middleware/
    RequestMetricsMiddleware.cs — capture timing after auth pipeline
  Controllers/V1/
    MetricsController.cs        — public endpoints
    AdminMetricsController.cs   — admin endpoints + percentile interpolation
  Storage/Entities/
    MetricMinuteEntity.cs, MetricHourEntity.cs,
    DailyActiveUserEntity.cs, ContainerStatusEntity.cs

Lovecraft.NotificationsWorker/
  Workers/
    MetricsRollupWorker.cs      — hourly :05 minute→hour rollup
    JanitorWorker.cs            — extended with 3 retention passes
    ContainerHeartbeatWorker.cs — duplicated from backend
  Configuration/
    WorkerMetricsConfig.cs      — worker-local config record
    WorkerMetricsConfigReader.cs — reads appconfig/metrics partition
  Entities/
    MetricMinuteEntity.cs, MetricHourEntity.cs,
    ContainerStatusEntity.cs    — duplicated entities

Lovecraft.TelegramBot/
  Workers/ContainerHeartbeatWorker.cs — duplicated heartbeat
  Storage/ContainerStatusEntity.cs    — duplicated entity
```

### Unit tests
- `Lovecraft.UnitTests/MockMetricsCollectorTests.cs` (5)
- `Lovecraft.UnitTests/AzureMetricsCollectorTests.cs` (4)
- `Lovecraft.UnitTests/DailyActiveUserCoalescerTests.cs` (4)
- `Lovecraft.UnitTests/RequestMetricsMiddlewareTests.cs` (6)
- `Lovecraft.UnitTests/ContainerHeartbeatWorkerTests.cs` (1)
- `Lovecraft.UnitTests/MetricsRollupWorkerTests.cs` (1)
- `Lovecraft.UnitTests/MetricsRetentionTests.cs` (4)
- `Lovecraft.UnitTests/MauCalculatorTests.cs` (1)
- `Lovecraft.UnitTests/MetricsControllerTests.cs` (2)
- `Lovecraft.UnitTests/AdminMetricsControllerTests.cs` (11)

Run: `dotnet test Lovecraft.UnitTests`.
