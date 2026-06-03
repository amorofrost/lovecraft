# Phase 2A — Backend FCM Notification Channel (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add a 5th notification channel `Fcm` to the LoveCraft backend so native Android devices can be registered and notifications enqueued for FCM delivery — using the **durable outbox-worker** path (like Telegram/Email, NOT in-process like Web Push). This phase ships everything **except real Firebase delivery**: a `StubFcmDispatcher` (logs + Delivered) stands in until credentials exist (mirrors `StubTelegram`/`StubEmail`). No Firebase project required to build or test.

**Architecture:** Mirror the existing Web Push subscription shape for registration (`fcmsubscriptions` table, PK=userId/RK=deviceId) and the Telegram/Email outbox path for delivery. `NotificationPolicy.ResolveChannels` adds `Fcm` when the user enabled it for the type **and** has ≥1 registered device; the producer then enqueues `OUTBOX_Fcm_PENDING` (it already routes any non-InApp/non-WebPush channel to the outbox). The worker's `OutboxProcessor` routes `"Fcm"` → `IFcmDispatcher`; `DispatcherWorker` drains the `Fcm` channel with the existing retry/dead-letter machinery.

**Tech Stack:** .NET 10 (SDK 10.0.103). xUnit + Moq. Build/test from `D:\src\lovecraft\Lovecraft`.

> **Verified patterns (read these to mirror):** `Services/MockPushSubscriptionService.cs` + `Services/Azure/AzurePushSubscriptionService.cs` + `Storage/Entities/WebPushSubscriptionEntity.cs` + `Common/DTOs/Notifications/WebPushSubscriptionDto.cs` (subscription CRUD); `Services/Notifications/NotificationPolicy.cs` (`ChannelAvailability` + `ResolveChannels`); `Services/Notifications/NotificationProducer.cs:183-193` (`BuildAvailabilityAsync`); `Services/MockNotificationPreferenceService.cs:33-52` (`BuildDefaults` matrix); `Controllers/V1/NotificationsController.cs` (the `/push/subscribe` endpoints to mirror); `Lovecraft.NotificationsWorker/Services/OutboxProcessor.cs:49-99` (channel→dispatcher switch) + `Workers/DispatcherWorker.cs:9` (`Channels`) + `Dispatchers/{ITelegramDispatcher,StubTelegramDispatcher}.cs`.
>
> **Commands** (from `D:\src\lovecraft\Lovecraft`): `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~<Class>"`; full: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj`. The whole solution (Backend + Worker + Common + Tests) must build.
>
> **Branch:** `feat/fcm-backend-channel` off `main`.
>
> **NotificationType values (9):** `LikeReceived, MatchCreated, MessageReceived, ForumReplyToThread, CommunityBroadcast, EventPublished, EventReminder, EventInviteReceived, RankUp`. **High-signal push defaults (fcm=true):** `MatchCreated, MessageReceived, LikeReceived, EventReminder`; all others fcm=false.

---

### Task 1: `Fcm` enum + DTOs + entity + table/store scaffolding

**Files:**
- Modify: `Lovecraft.Common/Enums/NotificationChannel.cs` (add `Fcm`)
- Create: `Lovecraft.Common/DTOs/Notifications/FcmSubscriptionDto.cs`
- Create: `Lovecraft.Backend/Storage/Entities/FcmSubscriptionEntity.cs`
- Modify: `Lovecraft.Backend/Storage/TableNames.cs` (add `FcmSubscriptions`)
- Modify: `Lovecraft.Backend/MockData/MockDataStore.cs` (add `FcmSubscriptions` store)

- [ ] **Step 1: Add the enum value**

In `Lovecraft.Common/Enums/NotificationChannel.cs`, add `Fcm` after `Email`:
```csharp
public enum NotificationChannel
{
    InApp,
    Telegram,
    WebPush,
    Email,
    Fcm,
}
```

- [ ] **Step 2: DTOs**

`Lovecraft.Common/DTOs/Notifications/FcmSubscriptionDto.cs`:
```csharp
namespace Lovecraft.Common.DTOs.Notifications;

/// <summary>A registered FCM device token for a user (one per app install).</summary>
public class FcmSubscriptionDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public string DeviceModel { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}

public class FcmRegisterRequestDto
{
    /// <summary>Stable per-install id; generated server-side if omitted.</summary>
    public string? DeviceId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public string DeviceModel { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Entity**

`Lovecraft.Backend/Storage/Entities/FcmSubscriptionEntity.cs`:
```csharp
using Azure;
using Azure.Data.Tables;

namespace Lovecraft.Backend.Storage.Entities;

/// <summary>PartitionKey = userId, RowKey = deviceId.</summary>
public class FcmSubscriptionEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public string DeviceModel { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}
```

- [ ] **Step 4: TableNames + MockDataStore**

- In `Lovecraft.Backend/Storage/TableNames.cs`, add a property mirroring the existing `WebPushSubscriptions` one (same `Prefix + "fcmsubscriptions"` style):
```csharp
    public static string FcmSubscriptions => $"{Prefix}fcmsubscriptions";
```
(match the exact casing/style of the neighboring entries; if they use a field+constant pattern, follow that instead.)
- In `Lovecraft.Backend/MockData/MockDataStore.cs`, add a store mirroring `PushSubscriptions`. Find the `PushSubscriptions` declaration (a `ConcurrentDictionary<(string UserId, string DeviceId), WebPushSubscriptionDto>`) and add next to it:
```csharp
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<(string UserId, string DeviceId), Lovecraft.Common.DTOs.Notifications.FcmSubscriptionDto> FcmSubscriptions = new();
```
(use the same namespacing style the file already uses — if it has `using` directives for these, use the short names.)

- [ ] **Step 5: Build + commit**

Run: `dotnet build Lovecraft.Backend/Lovecraft.Backend.csproj` (expect success).
```bash
cd /d/src/lovecraft
git add Lovecraft/Lovecraft.Common/Enums/NotificationChannel.cs Lovecraft/Lovecraft.Common/DTOs/Notifications/FcmSubscriptionDto.cs Lovecraft/Lovecraft.Backend/Storage/Entities/FcmSubscriptionEntity.cs Lovecraft/Lovecraft.Backend/Storage/TableNames.cs Lovecraft/Lovecraft.Backend/MockData/MockDataStore.cs
git -c user.name="amorofrost" -c user.email="amorofrost@gmail.com" commit -m "feat(fcm): NotificationChannel.Fcm + subscription DTO/entity/table scaffolding"
```

---

### Task 2: `IFcmSubscriptionService` (Mock + Azure) + DI + tests

**Files:**
- Modify: `Lovecraft.Backend/Services/IServices.cs` (add `IFcmSubscriptionService`) — OR wherever `IPushSubscriptionService` is declared
- Create: `Lovecraft.Backend/Services/MockFcmSubscriptionService.cs`
- Create: `Lovecraft.Backend/Services/Azure/AzureFcmSubscriptionService.cs`
- Modify: `Lovecraft.Backend/Program.cs` (register Mock + Azure)
- Test: `Lovecraft.UnitTests/FcmSubscriptionServiceTests.cs`

- [ ] **Step 1: Declare the interface**

Find where `IPushSubscriptionService` is declared (likely `Services/IServices.cs`) and add an analogous interface right after it:
```csharp
public interface IFcmSubscriptionService
{
    Task<FcmSubscriptionDto> RegisterAsync(string userId, FcmRegisterRequestDto request);
    Task<List<FcmSubscriptionDto>> ListAsync(string userId);
    Task<int> CountAsync(string userId);
    Task<bool> UnregisterAsync(string userId, string deviceId);
}
```
(add `using Lovecraft.Common.DTOs.Notifications;` to that file if not present.)

- [ ] **Step 2: Write the failing tests**

`Lovecraft.UnitTests/FcmSubscriptionServiceTests.cs` (mirror `PushSubscriptionServiceTests`):
```csharp
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Notifications;
using Xunit;

namespace Lovecraft.UnitTests;

[Collection("ChatTests")] // serialize: MockDataStore.FcmSubscriptions is shared static
public class FcmSubscriptionServiceTests
{
    private static MockFcmSubscriptionService Svc()
    {
        MockDataStore.FcmSubscriptions.Clear();
        return new MockFcmSubscriptionService();
    }

    private static FcmRegisterRequestDto Req(string? deviceId = "dev-1", string token = "tok-1") =>
        new() { DeviceId = deviceId, Token = token, Platform = "android", DeviceModel = "Pixel" };

    [Fact]
    public async Task Register_StoresToken()
    {
        var svc = Svc();
        var dto = await svc.RegisterAsync("u1", Req());
        Assert.Equal("dev-1", dto.DeviceId);
        Assert.Equal("tok-1", dto.Token);
        Assert.Equal(1, await svc.CountAsync("u1"));
    }

    [Fact]
    public async Task Register_GeneratesDeviceId_WhenOmitted()
    {
        var svc = Svc();
        var dto = await svc.RegisterAsync("u1", Req(deviceId: null));
        Assert.False(string.IsNullOrEmpty(dto.DeviceId));
    }

    [Fact]
    public async Task Register_SameDevice_UpdatesTokenAndKeepsCreatedAt()
    {
        var svc = Svc();
        var first = await svc.RegisterAsync("u1", Req(token: "old"));
        var second = await svc.RegisterAsync("u1", Req(token: "new"));
        Assert.Equal("new", second.Token);
        Assert.Equal(first.CreatedAtUtc, second.CreatedAtUtc);
        Assert.Equal(1, await svc.CountAsync("u1"));
    }

    [Fact]
    public async Task List_ReturnsOnlyUsersDevices()
    {
        var svc = Svc();
        await svc.RegisterAsync("u1", Req("d1"));
        await svc.RegisterAsync("u2", Req("d2"));
        var list = await svc.ListAsync("u1");
        Assert.Single(list);
        Assert.Equal("d1", list[0].DeviceId);
    }

    [Fact]
    public async Task Unregister_RemovesDevice()
    {
        var svc = Svc();
        await svc.RegisterAsync("u1", Req("d1"));
        Assert.True(await svc.UnregisterAsync("u1", "d1"));
        Assert.Equal(0, await svc.CountAsync("u1"));
    }

    [Fact]
    public async Task Unregister_ReturnsFalse_WhenAbsent()
    {
        var svc = Svc();
        Assert.False(await svc.UnregisterAsync("u1", "ghost"));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~FcmSubscriptionServiceTests"`
Expected: FAIL (compile — `MockFcmSubscriptionService` doesn't exist).

- [ ] **Step 4: Implement Mock + Azure services**

`Lovecraft.Backend/Services/MockFcmSubscriptionService.cs` (mirror `MockPushSubscriptionService`):
```csharp
using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Notifications;

namespace Lovecraft.Backend.Services;

public class MockFcmSubscriptionService : IFcmSubscriptionService
{
    public Task<FcmSubscriptionDto> RegisterAsync(string userId, FcmRegisterRequestDto request)
    {
        var deviceId = string.IsNullOrEmpty(request.DeviceId) ? Guid.NewGuid().ToString("N") : request.DeviceId;
        var now = DateTime.UtcNow;
        var dto = new FcmSubscriptionDto
        {
            DeviceId = deviceId,
            Token = request.Token,
            Platform = string.IsNullOrEmpty(request.Platform) ? "android" : request.Platform,
            DeviceModel = request.DeviceModel,
            CreatedAtUtc = MockDataStore.FcmSubscriptions.TryGetValue((userId, deviceId), out var existing)
                ? existing.CreatedAtUtc : now,
            LastSeenAtUtc = now,
        };
        MockDataStore.FcmSubscriptions[(userId, deviceId)] = dto;
        return Task.FromResult(dto);
    }

    public Task<List<FcmSubscriptionDto>> ListAsync(string userId) =>
        Task.FromResult(MockDataStore.FcmSubscriptions
            .Where(kv => kv.Key.UserId == userId)
            .Select(kv => kv.Value)
            .ToList());

    public Task<int> CountAsync(string userId) =>
        Task.FromResult(MockDataStore.FcmSubscriptions.Count(kv => kv.Key.UserId == userId));

    public Task<bool> UnregisterAsync(string userId, string deviceId) =>
        Task.FromResult(MockDataStore.FcmSubscriptions.TryRemove((userId, deviceId), out _));
}
```

`Lovecraft.Backend/Services/Azure/AzureFcmSubscriptionService.cs` — **read `AzurePushSubscriptionService.cs` and mirror it exactly** (same `TableClient` construction from connection string + `TableNames`, `CreateIfNotExistsAsync` on startup, the upsert/list/count/delete operations), substituting `FcmSubscriptionEntity`/`FcmSubscriptionDto`/`TableNames.FcmSubscriptions` and mapping the `Token`/`Platform`/`DeviceModel` fields. `RegisterAsync` upserts (preserving `CreatedAtUtc` if the row exists); `CountAsync` counts the partition; `UnregisterAsync` deletes by (userId, deviceId).

- [ ] **Step 5: Register in DI**

In `Lovecraft.Backend/Program.cs`, find where `IPushSubscriptionService` is registered (both the `USE_AZURE_STORAGE` true and false branches) and add the FCM analog right beside each:
- Azure branch: `builder.Services.AddSingleton<IFcmSubscriptionService, AzureFcmSubscriptionService>(...);` (match the exact factory style used for `AzurePushSubscriptionService`).
- Mock branch: `builder.Services.AddSingleton<IFcmSubscriptionService, MockFcmSubscriptionService>();`

- [ ] **Step 6: Run to verify pass + commit**

Run: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~FcmSubscriptionServiceTests"`
Expected: PASS (6).
```bash
cd /d/src/lovecraft
git add Lovecraft/Lovecraft.Backend/Services/IServices.cs Lovecraft/Lovecraft.Backend/Services/MockFcmSubscriptionService.cs Lovecraft/Lovecraft.Backend/Services/Azure/AzureFcmSubscriptionService.cs Lovecraft/Lovecraft.Backend/Program.cs Lovecraft/Lovecraft.UnitTests/FcmSubscriptionServiceTests.cs
git -c user.name="amorofrost" -c user.email="amorofrost@gmail.com" commit -m "feat(fcm): IFcmSubscriptionService (Mock + Azure) + DI"
```

---

### Task 3: Policy + availability + producer wiring + preference defaults

**Files:**
- Modify: `Lovecraft.Backend/Services/Notifications/NotificationPolicy.cs` (`ChannelAvailability.FcmRegistered` + `ResolveChannels`)
- Modify: `Lovecraft.Backend/Services/Notifications/NotificationProducer.cs` (inject `IFcmSubscriptionService`, set `FcmRegistered`)
- Modify: `Lovecraft.Backend/Services/MockNotificationPreferenceService.cs` (`BuildDefaults` fcm cell + frequency)
- Test: `Lovecraft.UnitTests/NotificationPolicyFcmTests.cs`

- [ ] **Step 1: Write the failing policy tests**

`Lovecraft.UnitTests/NotificationPolicyFcmTests.cs`:
```csharp
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Xunit;

namespace Lovecraft.UnitTests;

public class NotificationPolicyFcmTests
{
    private static NotificationPreferencesDto PrefsWithFcm(bool fcm)
    {
        var p = new NotificationPreferencesDto();
        p.Matrix["matchCreated"] = new() { ["inApp"] = true, ["fcm"] = fcm };
        return p;
    }

    [Fact]
    public void Fcm_Added_WhenEnabled_AndRegistered()
    {
        var channels = NotificationPolicy.ResolveChannels(
            PrefsWithFcm(true), NotificationType.MatchCreated,
            new ChannelAvailability { FcmRegistered = true });
        Assert.Contains(NotificationChannel.Fcm, channels);
    }

    [Fact]
    public void Fcm_NotAdded_WhenNoDeviceRegistered()
    {
        var channels = NotificationPolicy.ResolveChannels(
            PrefsWithFcm(true), NotificationType.MatchCreated,
            new ChannelAvailability { FcmRegistered = false });
        Assert.DoesNotContain(NotificationChannel.Fcm, channels);
    }

    [Fact]
    public void Fcm_NotAdded_WhenDisabledInPrefs()
    {
        var channels = NotificationPolicy.ResolveChannels(
            PrefsWithFcm(false), NotificationType.MatchCreated,
            new ChannelAvailability { FcmRegistered = true });
        Assert.DoesNotContain(NotificationChannel.Fcm, channels);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~NotificationPolicyFcmTests"`
Expected: FAIL (`FcmRegistered` doesn't exist).

- [ ] **Step 3: Wire the policy**

In `NotificationPolicy.cs`:
- add to `ChannelAvailability`: `public bool FcmRegistered { get; set; }`
- in `ResolveChannels`, after the `email` block, add:
```csharp
        if (Enabled(row, "fcm") && avail.FcmRegistered)
            result.Add(NotificationChannel.Fcm);
```

- [ ] **Step 4: Wire the producer**

In `NotificationProducer.cs`:
- add a field `private readonly IFcmSubscriptionService _fcm;` and a constructor param `IFcmSubscriptionService fcm` assigned `_fcm = fcm;` (add it to the constructor parameter list + assignment).
- in `BuildAvailabilityAsync`, add `var fcmCount = await _fcm.CountAsync(userId);` and set `FcmRegistered = fcmCount > 0` on the returned `ChannelAvailability`.
- **Note:** any test/code constructing `NotificationProducer` directly must pass the new arg. Grep `new NotificationProducer(` across the tests and update those constructions (pass a `MockFcmSubscriptionService` or a `Mock<IFcmSubscriptionService>`).

- [ ] **Step 5: Preference defaults**

In `MockNotificationPreferenceService.BuildDefaults`, add an `fcm` cell to the per-type matrix (high-signal types default true) and a frequency:
```csharp
            prefs.Matrix[key] = new Dictionary<string, bool>
            {
                { "inApp",    true  },
                { "telegram", false },
                { "webPush",  false },
                { "email",    false },
                { "fcm",      name is "MatchCreated" or "MessageReceived" or "LikeReceived" or "EventReminder" },
            };
```
and after the existing frequency lines: `prefs.Frequency["fcm"] = NotificationFrequency.Immediate;`

- [ ] **Step 6: Run + commit**

Run: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~NotificationPolicyFcmTests|FullyQualifiedName~NotificationProducer"`
Expected: PASS (the 3 new + existing producer tests still green after the constructor change).
```bash
cd /d/src/lovecraft
git add Lovecraft/Lovecraft.Backend/Services/Notifications/NotificationPolicy.cs Lovecraft/Lovecraft.Backend/Services/Notifications/NotificationProducer.cs Lovecraft/Lovecraft.Backend/Services/MockNotificationPreferenceService.cs Lovecraft/Lovecraft.UnitTests/NotificationPolicyFcmTests.cs Lovecraft/Lovecraft.UnitTests/
git -c user.name="amorofrost" -c user.email="amorofrost@gmail.com" commit -m "feat(fcm): resolve Fcm channel (enabled + registered) + availability + prefs defaults"
```

---

### Task 4: Register/unregister endpoints

**Files:**
- Modify: `Lovecraft.Backend/Controllers/V1/NotificationsController.cs` (add `POST/DELETE /push/fcm/register`)
- Test: `Lovecraft.UnitTests/FcmRegistrationEndpointTests.cs`

- [ ] **Step 1: Add the endpoints**

In `NotificationsController.cs`, **mirror the existing Web Push `POST /push/subscribe` + `DELETE /push/subscribe/{deviceId}` endpoints** (same `[Authorize]`, same way they resolve the caller's user id from `ClaimTypes.NameIdentifier`, same `ApiResponse<T>` wrapper), injecting `IFcmSubscriptionService` (add it to the controller constructor + field):
```csharp
    [HttpPost("/api/v1/push/fcm/register")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<FcmSubscriptionDto>>> RegisterFcm([FromBody] FcmRegisterRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiResponse<FcmSubscriptionDto>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(ApiResponse<FcmSubscriptionDto>.ErrorResponse("INVALID_TOKEN", "FCM token is required"));
        var dto = await _fcm.RegisterAsync(userId, request);
        return Ok(ApiResponse<FcmSubscriptionDto>.SuccessResponse(dto));
    }

    [HttpDelete("/api/v1/push/fcm/register/{deviceId}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> UnregisterFcm(string deviceId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiResponse<bool>.ErrorResponse("UNAUTHORIZED", "Not authenticated"));
        var removed = await _fcm.UnregisterAsync(userId, deviceId);
        return Ok(ApiResponse<bool>.SuccessResponse(removed));
    }
```
(Use absolute route templates `[HttpPost("/api/v1/push/fcm/register")]` if the controller's `[Route]` prefix is `api/v1/notifications` — the existing Web Push endpoints already do this for `/push/...`; match whatever they do. Add `_fcm` to the constructor like the other injected services; ensure `using Lovecraft.Common.DTOs.Notifications;` + `using System.Security.Claims;` are present.)

- [ ] **Step 2: Write integration tests (mirror an existing `AclTests.TestAppFactory`-based test)**

`Lovecraft.UnitTests/FcmRegistrationEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Lovecraft.UnitTests;

[Collection("ChatNotificationTests")]
public class FcmRegistrationEndpointTests : IClassFixture<AclTests.TestAppFactory>
{
    private readonly AclTests.TestAppFactory _factory;
    public FcmRegistrationEndpointTests(AclTests.TestAppFactory factory) => _factory = factory;

    private HttpClient Client(string userId)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-User", userId);
        c.DefaultRequestHeaders.Add("X-Test-StaffRole", "none");
        return c;
    }

    [Fact]
    public async Task Register_then_unregister_roundtrip()
    {
        using var client = Client("fcm-u1");
        var reg = await client.PostAsJsonAsync("/api/v1/push/fcm/register",
            new { deviceId = "d1", token = "tok-abc", platform = "android", deviceModel = "Pixel 8" });
        reg.EnsureSuccessStatusCode();
        var body = await reg.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("d1", body.GetProperty("data").GetProperty("deviceId").GetString());

        var del = await client.DeleteAsync("/api/v1/push/fcm/register/d1");
        del.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Register_rejects_missing_token()
    {
        using var client = Client("fcm-u2");
        var reg = await client.PostAsJsonAsync("/api/v1/push/fcm/register",
            new { deviceId = "d2", token = "", platform = "android" });
        Assert.Equal(HttpStatusCode.BadRequest, reg.StatusCode);
    }

    [Fact]
    public async Task Register_requires_auth()
    {
        var client = _factory.CreateClient(); // no X-Test-User header
        var reg = await client.PostAsJsonAsync("/api/v1/push/fcm/register",
            new { token = "tok" });
        Assert.Equal(HttpStatusCode.Unauthorized, reg.StatusCode);
    }
}
```
(If the test auth handler treats a missing `X-Test-User` as a default user rather than 401, drop the third test or adjust to match how the existing endpoint-auth tests assert unauthenticated access — check `AclTests`.)

- [ ] **Step 3: Run + commit**

Run: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~FcmRegistrationEndpointTests"`
Expected: PASS.
```bash
cd /d/src/lovecraft
git add Lovecraft/Lovecraft.Backend/Controllers/V1/NotificationsController.cs Lovecraft/Lovecraft.UnitTests/FcmRegistrationEndpointTests.cs
git -c user.name="amorofrost" -c user.email="amorofrost@gmail.com" commit -m "feat(fcm): POST/DELETE /push/fcm/register endpoints"
```

---

### Task 5: Worker FCM dispatch (stub) + outbox routing

**Files:**
- Create: `Lovecraft.NotificationsWorker/Dispatchers/IFcmDispatcher.cs`
- Create: `Lovecraft.NotificationsWorker/Dispatchers/StubFcmDispatcher.cs`
- Modify: `Lovecraft.NotificationsWorker/Services/OutboxProcessor.cs` (inject `IFcmDispatcher`, add `"Fcm"` route)
- Modify: `Lovecraft.NotificationsWorker/Workers/DispatcherWorker.cs` (add `"Fcm"` to `Channels`)
- Modify: `Lovecraft.NotificationsWorker/Program.cs` (register `IFcmDispatcher`)
- Modify: `Lovecraft.UnitTests/` — update any `new OutboxProcessor(...)` construction + add a Fcm-routing test

- [ ] **Step 1: Dispatcher interface + stub**

`Lovecraft.NotificationsWorker/Dispatchers/IFcmDispatcher.cs`:
```csharp
using Lovecraft.NotificationsWorker.Models;

namespace Lovecraft.NotificationsWorker.Dispatchers;

public interface IFcmDispatcher
{
    Task<DispatchResult> DispatchAsync(NotificationModel notification, CancellationToken ct);
}
```

`Lovecraft.NotificationsWorker/Dispatchers/StubFcmDispatcher.cs` (mirror `StubTelegramDispatcher`):
```csharp
using Lovecraft.NotificationsWorker.Models;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Dispatchers;

/// <summary>
/// Stand-in until Firebase credentials exist (Phase 2C). Logs the dispatch and returns Delivered.
/// Replace with a real FcmDispatcher (Firebase Admin / FCM HTTP v1, data messages, dead-token pruning)
/// once FCM_SERVICE_ACCOUNT_JSON is configured.
/// </summary>
public class StubFcmDispatcher : IFcmDispatcher
{
    private readonly ILogger<StubFcmDispatcher> _logger;
    public StubFcmDispatcher(ILogger<StubFcmDispatcher> logger) => _logger = logger;

    public Task<DispatchResult> DispatchAsync(NotificationModel notification, CancellationToken ct)
    {
        _logger.LogInformation(
            "[STUB Fcm] would dispatch notification {NotificationId} ({Type}) to user {UserId}",
            notification.NotificationId, notification.Type, notification.UserId);
        return Task.FromResult(DispatchResult.Delivered);
    }
}
```

- [ ] **Step 2: Route the channel in `OutboxProcessor`**

In `OutboxProcessor.cs`:
- add a field `private readonly IFcmDispatcher _fcm;` + constructor param `IFcmDispatcher fcm` assigned `_fcm = fcm;`
- in the `channel switch` (the `result = channel switch { ... }`), add: `"Fcm" => await _fcm.DispatchAsync(notification, ct),` (before the `_ => DispatchResult.PermanentError` default).

- [ ] **Step 3: Add the channel to the drain loop**

In `DispatcherWorker.cs`, change `Channels`:
```csharp
    private static readonly string[] Channels = { "Telegram", "Email", "Fcm" };
```

- [ ] **Step 4: Register the dispatcher in the worker**

In `Lovecraft.NotificationsWorker/Program.cs`, register `IFcmDispatcher`. **Mirror how `ITelegramDispatcher` is registered** (it picks the real vs stub based on whether the bot token / credentials env var is set). For now register the stub, leaving a clear seam for the real one:
```csharp
// Phase 2A: stub until FCM_SERVICE_ACCOUNT_JSON is configured (Phase 2C swaps in the real FcmDispatcher).
builder.Services.AddSingleton<IFcmDispatcher, StubFcmDispatcher>();
```
(If Telegram uses an `if (env present) AddSingleton<Real> else AddSingleton<Stub>` pattern, write the same `if/else` keyed on `FCM_SERVICE_ACCOUNT_JSON` with the real branch `throw`ing a "not yet implemented" or simply both branches → stub for now, with a `// TODO 2C` comment. Keep it building.)

- [ ] **Step 5: Update `OutboxProcessorTests` construction + add a routing test**

- `grep -rn "new OutboxProcessor(" Lovecraft/Lovecraft.UnitTests` — every construction needs the new `IFcmDispatcher` arg (pass a `Mock<IFcmDispatcher>().Object` or a `StubFcmDispatcher`).
- Add a test asserting an `OUTBOX_Fcm_PENDING` row is dispatched via the FCM dispatcher (mirror the existing Telegram/Email routing test in `OutboxProcessorTests`): enqueue an `Fcm` immediate row, set the `Mock<IFcmDispatcher>` to return `Delivered`, run `ProcessChannelAsync("Fcm", ...)`, and verify the dispatcher was invoked + the row moved to DONE. (Match the existing test's table setup.)

- [ ] **Step 6: Build the worker + run worker tests + commit**

Run: `dotnet build Lovecraft.NotificationsWorker/Lovecraft.NotificationsWorker.csproj` then `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~OutboxProcessor"`
Expected: build success + PASS.
```bash
cd /d/src/lovecraft
git add Lovecraft/Lovecraft.NotificationsWorker/ Lovecraft/Lovecraft.UnitTests/
git -c user.name="amorofrost" -c user.email="amorofrost@gmail.com" commit -m "feat(fcm): worker outbox routing + StubFcmDispatcher (real dispatcher deferred to 2C)"
```

---

### Task 6: Full solution build + regression

**Files:** none.

- [ ] **Step 1: Build the whole solution + run the full suite**

Run (from `D:\src\lovecraft\Lovecraft`):
```bash
dotnet build Lovecraft.slnx
dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj
```
Expected: all projects build (Backend + NotificationsWorker + Common + Tools + Tests); all tests PASS (the prior 712 + the new FCM tests). If any pre-existing test regressed, a constructor/DI change leaked behavior — investigate.

---

## Self-review checklist (author)

- **Spec coverage (android spec §3, §3.1):** `fcmsubscriptions` table + entity → Task 1; register endpoints → Task 4; `IFcmSubscriptionService` (Azure+Mock) → Task 2; `NotificationChannel.Fcm` + policy (`enabled && registered`) + availability + prefs defaults → Tasks 1,3; **outbox-worker delivery** (NOT in-process) → producer's existing else-branch enqueues `OUTBOX_Fcm_PENDING` (Task 3 makes `Fcm` resolvable) + `OutboxProcessor` routes `"Fcm"` + `DispatcherWorker` drains it (Task 5). **Deferred to 2C (needs Firebase creds):** the real `FcmDispatcher` (Firebase Admin SDK, data messages with `collapse_key`/`android.tag = notificationId`, dead-token pruning) — `StubFcmDispatcher` stands in; the DI seam is in worker `Program.cs`.
- **Placeholder scan:** none — concrete code + commands; the few "mirror X" steps name the exact file to copy and the substitutions.
- **Backward-compat:** new channel is opt-in per type (defaults on only for 4 high-signal types) AND requires ≥1 registered device, so existing users with no Android device get no `Fcm` outbox rows. Producer/OutboxProcessor constructor changes are matched by updating their test constructions (Tasks 3,5).
- **Type consistency:** `IFcmSubscriptionService.{Register,List,Count,Unregister}` used by the controller (Task 4) + producer availability (Task 3); `FcmRegisterRequestDto`/`FcmSubscriptionDto` shared controller↔service↔tests; `NotificationChannel.Fcm` resolved in policy → enqueued by producer → routed by `OutboxProcessor` `"Fcm"` → drained by `DispatcherWorker` `"Fcm"` → `IFcmDispatcher`.

## Follow-on

- **2B — Android in-app notifications + SignalR realtime** (Firebase-free): the `com.microsoft.signalr` connection (`NotificationReceived`), bell/feed/preferences UI, the §7.3 dedup ledger (Room), REST reconciliation.
- **2C — real FCM** (needs Firebase): swap `StubFcmDispatcher` → real `FcmDispatcher` (Firebase Admin SDK, reads `fcmsubscriptions`, data messages, dead-token pruning) wired on `FCM_SERVICE_ACCOUNT_JSON`; Android `FirebaseMessagingService` + channels + device-token registration + deep links + the Android-13 permission.
