# §3.2 — SignalR `JoinTopic` Authorization (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Close a source-verified authorization hole: `ChatHub.JoinTopic` has **no access check**, while `ForumController.CreateReply` broadcasts full reply DTOs to the `topic-{topicId}` group. Any authenticated user can join a restricted (attendees-only / specific-users / rank-gated / novice-hidden) topic group and receive live replies they cannot read via REST. Fix by extracting the topic-view logic into a shared `IForumTopicAccess` (single source of truth) and gating `JoinTopic` with it.

**Architecture:** New `IForumTopicAccess` helper combining the three existing checks (event-topic visibility via `EventTopicAccess`, `MinRank` via `PermissionGuard`, `NoviceVisible`). `ChatHub.JoinTopic` calls `CanViewTopicAsync(user, topicId)` and throws `HubException` when denied. `ForumController` delegates its event-topic check to the same helper so the two paths can't drift. Backward-compatible: legitimate clients only join topics they can already see; the controller keeps its distinct REST error codes.

**Tech Stack:** .NET 10 (10.0.103 SDK installed), xUnit + Moq. Build/test from `D:\src\lovecraft\Lovecraft`.

> **Source-verified context:** `Hubs/ChatHub.cs:40-47` (no check, comment admits it); `Controllers/V1/ForumController.cs:308` (broadcast), `:482-513` (`CallerMayAccessEventTopicContentAsync` + `ResolveEventIdFromTopic`), `GetTopic`/`GetReplies` layer `PermissionGuard.MeetsAsync(MinRank)` + `NoviceVisible`. `IUserService`/`IEventService`/`IForumService` are DI **singletons**. `PermissionGuard.MeetsAsync(ClaimsPrincipal, IUserService, string)`. `ForumTopicDto.MinRank` is a string; `.NoviceVisible` bool; `.SectionId`/`.EventId`. `EventTopicAccess.CanViewEventTopic(EventDto, ForumTopicDto, userId, isElevated)` is a static helper. `IPresenceTracker.Join/Leave` are `void`.
>
> **Commands** (from `D:\src\lovecraft\Lovecraft`):
> - test a class: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~ForumTopicAccessTests"`
> - full: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj`
> - First run restores + builds the whole backend (allow a few minutes).
>
> **Branch:** `fix/jointopic-authorization` off the repo's default branch.

---

### Task 1: `IForumTopicAccess` helper + unit tests

**Files:**
- Create: `Lovecraft.Backend/Helpers/ForumTopicAccess.cs`
- Test: `Lovecraft.UnitTests/ForumTopicAccessTests.cs`

- [ ] **Step 1: Write the failing tests**

`Lovecraft.UnitTests/ForumTopicAccessTests.cs`:
```csharp
using System.Security.Claims;
using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Events;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class ForumTopicAccessTests
{
    private static ClaimsPrincipal User(string id, string staffRole = "none") =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim("staffRole", staffRole),
        }, "test"));

    private static ForumTopicDto EventTopic(EventTopicVisibility vis, string id = "event-topic-e1") => new()
    {
        Id = id, SectionId = "events", EventId = "e1",
        EventTopicVisibility = vis, MinRank = "novice", NoviceVisible = true,
    };

    private static (ForumTopicAccess access, Mock<IForumService> forum, Mock<IEventService> events, Mock<IUserService> users) Build()
    {
        var forum = new Mock<IForumService>();
        var events = new Mock<IEventService>();
        var users = new Mock<IUserService>();
        // Default: any user resolves to a high rank (so MinRank/NoviceVisible don't accidentally gate).
        users.Setup(u => u.GetUserByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new UserDto { Id = id, Rank = UserRank.AloeCrew });
        return (new ForumTopicAccess(forum.Object, events.Object, users.Object), forum, events, users);
    }

    [Fact]
    public async Task AttendeesOnly_NonAttendee_CannotViewTopic()
    {
        var (access, forum, events, _) = Build();
        var topic = EventTopic(EventTopicVisibility.AttendeesOnly);
        forum.Setup(f => f.GetTopicByIdAsync(topic.Id)).ReturnsAsync(topic);
        events.Setup(e => e.GetEventByIdAdminAsync("e1"))
            .ReturnsAsync(new EventDto { Id = "e1", Attendees = new List<string> { "a", "b" } });

        Assert.False(await access.CanViewTopicAsync(User("intruder"), topic.Id));
    }

    [Fact]
    public async Task AttendeesOnly_Attendee_CanViewTopic()
    {
        var (access, forum, events, _) = Build();
        var topic = EventTopic(EventTopicVisibility.AttendeesOnly);
        forum.Setup(f => f.GetTopicByIdAsync(topic.Id)).ReturnsAsync(topic);
        events.Setup(e => e.GetEventByIdAdminAsync("e1"))
            .ReturnsAsync(new EventDto { Id = "e1", Attendees = new List<string> { "a", "b" } });

        Assert.True(await access.CanViewTopicAsync(User("a"), topic.Id));
    }

    [Fact]
    public async Task AttendeesOnly_Moderator_CanViewTopic()
    {
        var (access, forum, events, _) = Build();
        var topic = EventTopic(EventTopicVisibility.AttendeesOnly);
        forum.Setup(f => f.GetTopicByIdAsync(topic.Id)).ReturnsAsync(topic);
        events.Setup(e => e.GetEventByIdAdminAsync("e1"))
            .ReturnsAsync(new EventDto { Id = "e1", Attendees = new List<string>() });

        Assert.True(await access.CanViewTopicAsync(User("mod", "moderator"), topic.Id));
    }

    [Fact]
    public async Task NoviceHiddenTopic_NoviceCannotView()
    {
        var (access, forum, _, users) = Build();
        var topic = new ForumTopicDto { Id = "t1", SectionId = "general", MinRank = "novice", NoviceVisible = false };
        forum.Setup(f => f.GetTopicByIdAsync("t1")).ReturnsAsync(topic);
        users.Setup(u => u.GetUserByIdAsync("nov")).ReturnsAsync(new UserDto { Id = "nov", Rank = UserRank.Novice });

        Assert.False(await access.CanViewTopicAsync(User("nov"), "t1"));
    }

    [Fact]
    public async Task PublicGeneralTopic_AnyUserCanView()
    {
        var (access, forum, _, _) = Build();
        var topic = new ForumTopicDto { Id = "t2", SectionId = "general", MinRank = "novice", NoviceVisible = true };
        forum.Setup(f => f.GetTopicByIdAsync("t2")).ReturnsAsync(topic);

        Assert.True(await access.CanViewTopicAsync(User("anyone"), "t2"));
    }

    [Fact]
    public async Task MissingTopic_CannotView()
    {
        var (access, forum, _, _) = Build();
        forum.Setup(f => f.GetTopicByIdAsync("ghost")).ReturnsAsync((ForumTopicDto?)null);
        Assert.False(await access.CanViewTopicAsync(User("u"), "ghost"));
    }

    [Fact]
    public async Task SpecificUsers_OnlyListedCanView()
    {
        var (access, forum, events, _) = Build();
        var topic = EventTopic(EventTopicVisibility.SpecificUsers);
        topic.AllowedUserIds = new List<string> { "x" };
        forum.Setup(f => f.GetTopicByIdAsync(topic.Id)).ReturnsAsync(topic);
        events.Setup(e => e.GetEventByIdAdminAsync("e1")).ReturnsAsync(new EventDto { Id = "e1" });

        Assert.True(await access.CanViewTopicAsync(User("x"), topic.Id));
        Assert.False(await access.CanViewTopicAsync(User("z"), topic.Id));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~ForumTopicAccessTests"`
Expected: FAIL — `ForumTopicAccess` / `IForumTopicAccess` don't exist (compile error).

- [ ] **Step 3: Implement the helper**

`Lovecraft.Backend/Helpers/ForumTopicAccess.cs`:
```csharp
using System.Security.Claims;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Forum;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Helpers;

/// <summary>
/// Single source of truth for "may this caller view this forum topic", combining event-topic
/// visibility, MinRank, and NoviceVisible. Shared by ForumController (REST) and ChatHub (SignalR
/// JoinTopic) so the two authorization paths can't drift.
/// </summary>
public interface IForumTopicAccess
{
    /// <summary>Event-topic visibility check only (true for non-event sections). The controller uses
    /// this and layers its own MinRank / NoviceVisible checks with distinct REST error codes.</summary>
    Task<bool> CanViewEventTopicContentAsync(ClaimsPrincipal user, ForumTopicDto topic);

    /// <summary>Full combined check (event-topic visibility + MinRank + NoviceVisible). The hub uses
    /// this — it only needs a boolean.</summary>
    Task<bool> CanViewTopicAsync(ClaimsPrincipal user, string topicId);
}

public sealed class ForumTopicAccess : IForumTopicAccess
{
    private readonly IForumService _forum;
    private readonly IEventService _events;
    private readonly IUserService _users;

    public ForumTopicAccess(IForumService forum, IEventService events, IUserService users)
    {
        _forum = forum;
        _events = events;
        _users = users;
    }

    public async Task<bool> CanViewEventTopicContentAsync(ClaimsPrincipal user, ForumTopicDto topic)
    {
        if (!topic.SectionId.Equals("events", StringComparison.OrdinalIgnoreCase))
            return true;

        var eventId = ResolveEventIdFromTopic(topic);
        if (string.IsNullOrEmpty(eventId)) return false;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return false;

        var staff = user.FindFirst("staffRole")?.Value ?? "none";
        var isElevated = staff is "moderator" or "admin";

        var ev = await _events.GetEventByIdAdminAsync(eventId);
        if (ev is null) return false;

        return EventTopicAccess.CanViewEventTopic(ev, topic, userId, isElevated);
    }

    public async Task<bool> CanViewTopicAsync(ClaimsPrincipal user, string topicId)
    {
        var topic = await _forum.GetTopicByIdAsync(topicId);
        if (topic is null) return false;

        if (!await CanViewEventTopicContentAsync(user, topic)) return false;

        if (!await PermissionGuard.MeetsAsync(user, _users, topic.MinRank)) return false;

        if (!topic.NoviceVisible)
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var rank = (string.IsNullOrEmpty(userId) ? null : (await _users.GetUserByIdAsync(userId))?.Rank)
                       ?? UserRank.Novice;
            if (rank == UserRank.Novice) return false;
        }

        return true;
    }

    /// <summary>Resolve the owning event id from an event-section topic (moved here from
    /// ForumController so both paths share one implementation).</summary>
    public static string? ResolveEventIdFromTopic(ForumTopicDto t)
    {
        if (!string.IsNullOrEmpty(t.EventId))
            return t.EventId;
        if (t.Id.StartsWith("evt-", StringComparison.Ordinal) && t.Id.Length > 4)
            return t.Id.Substring(4);
        if (t.Id.StartsWith("event-topic-", StringComparison.Ordinal) && t.Id.Length > "event-topic-".Length)
            return t.Id["event-topic-".Length..];
        return null;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~ForumTopicAccessTests"`
Expected: PASS (8 tests). If a referenced member differs (e.g. `IEventService.GetEventByIdAdminAsync`, `UserDto.Rank`), fix to match the real signature and report; do not weaken the tests.

- [ ] **Step 5: Commit**

```bash
cd /d/src/lovecraft
git add Lovecraft/Lovecraft.Backend/Helpers/ForumTopicAccess.cs Lovecraft/Lovecraft.UnitTests/ForumTopicAccessTests.cs
git -c user.name="amorofrost" -c user.email="amorofrost@gmail.com" commit -m "feat: IForumTopicAccess — shared topic-view authorization helper"
```

---

### Task 2: Gate `ChatHub.JoinTopic` + delegate `ForumController` + DI + hub test

**Files:**
- Modify: `Lovecraft.Backend/Hubs/ChatHub.cs` (inject `IForumTopicAccess`, gate `JoinTopic`)
- Modify: `Lovecraft.Backend/Controllers/V1/ForumController.cs` (inject helper; delegate `CallerMayAccessEventTopicContentAsync`; remove now-duplicate private `ResolveEventIdFromTopic`)
- Modify: `Lovecraft.Backend/Program.cs` (register `IForumTopicAccess`)
- Test: `Lovecraft.UnitTests/ChatHubJoinTopicTests.cs`

- [ ] **Step 1: Write the failing hub test**

`Lovecraft.UnitTests/ChatHubJoinTopicTests.cs`:
```csharp
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Hubs;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class ChatHubJoinTopicTests
{
    private static ClaimsPrincipal User(string id) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }, "test"));

    private static ChatHub BuildHub(Mock<IForumTopicAccess> access, Mock<IGroupManager> groups)
    {
        var chat = new Mock<IChatService>();
        var presence = new Mock<IPresenceTracker>();
        var ctx = new Mock<HubCallerContext>();
        ctx.SetupGet(c => c.User).Returns(User("u1"));
        ctx.SetupGet(c => c.ConnectionId).Returns("conn-1");
        return new ChatHub(chat.Object, presence.Object, access.Object)
        {
            Context = ctx.Object,
            Groups = groups.Object,
        };
    }

    [Fact]
    public async Task JoinTopic_Throws_And_DoesNotJoin_WhenAccessDenied()
    {
        var access = new Mock<IForumTopicAccess>();
        access.Setup(a => a.CanViewTopicAsync(It.IsAny<ClaimsPrincipal>(), "event-attendees-e1")).ReturnsAsync(false);
        var groups = new Mock<IGroupManager>();
        var hub = BuildHub(access, groups);

        await Assert.ThrowsAsync<HubException>(() => hub.JoinTopic("event-attendees-e1"));
        groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JoinTopic_AddsToGroup_WhenAllowed()
    {
        var access = new Mock<IForumTopicAccess>();
        access.Setup(a => a.CanViewTopicAsync(It.IsAny<ClaimsPrincipal>(), "topic-ok")).ReturnsAsync(true);
        var groups = new Mock<IGroupManager>();
        groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        var hub = BuildHub(access, groups);

        await hub.JoinTopic("topic-ok");
        groups.Verify(g => g.AddToGroupAsync("conn-1", "topic-topic-ok", It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~ChatHubJoinTopicTests"`
Expected: FAIL — `ChatHub` has no 3-arg constructor yet (compile error).

- [ ] **Step 3: Gate the hub**

In `Lovecraft.Backend/Hubs/ChatHub.cs`:
- add the field + constructor param:
```csharp
    private readonly IChatService _chatService;
    private readonly IPresenceTracker _presence;
    private readonly Lovecraft.Backend.Helpers.IForumTopicAccess _topicAccess;
```
```csharp
    public ChatHub(IChatService chatService, IPresenceTracker presence, Lovecraft.Backend.Helpers.IForumTopicAccess topicAccess)
    {
        _chatService = chatService;
        _presence = presence;
        _topicAccess = topicAccess;
    }
```
- replace the body of `JoinTopic`:
```csharp
    public async Task JoinTopic(string topicId)
    {
        if (!await _topicAccess.CanViewTopicAsync(Context.User!, topicId))
        {
            throw new HubException("Access denied to topic.");
        }
        var groupName = $"topic-{topicId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _presence.Join(groupName, CurrentUserId);
        RecordConnectionGroup(Context.ConnectionId, groupName);
    }
```
(Remove the old `// No access check…` comment.)

- [ ] **Step 4: Delegate the controller's event-topic check to the helper**

In `Lovecraft.Backend/Controllers/V1/ForumController.cs`:
- add a field + constructor param `IForumTopicAccess topicAccess` (alongside the existing `_forumService`, `_eventService`, `_userService`, etc.), assigned in the constructor as `_topicAccess = topicAccess;`.
- replace the private helper body:
```csharp
    private Task<bool> CallerMayAccessEventTopicContentAsync(ForumTopicDto topic)
        => _topicAccess.CanViewEventTopicContentAsync(User, topic);
```
- **delete** the now-unused private `ResolveEventIdFromTopic` method (it lives on `ForumTopicAccess` now). Confirm it has no other references in the controller (`grep ResolveEventIdFromTopic` → only the deleted definition). Add `using Lovecraft.Backend.Helpers;` if not present.
- Leave `GetTopic`/`GetReplies`/`CreateReply`'s separate `PermissionGuard.MeetsAsync` + `NoviceVisible` checks (with their `INSUFFICIENT_RANK` codes) **unchanged** — only the event-topic portion is delegated. Behavior is identical, so existing `AclTests`/`ForumTests` must still pass.

- [ ] **Step 5: Register the helper in DI**

In `Lovecraft.Backend/Program.cs`, add (registration order is irrelevant; near `builder.Services.AddSignalR();` is fine — it resolves the singleton `IForumService`/`IEventService`/`IUserService`):
```csharp
builder.Services.AddSingleton<Lovecraft.Backend.Helpers.IForumTopicAccess, Lovecraft.Backend.Helpers.ForumTopicAccess>();
```

- [ ] **Step 6: Build + run hub tests**

Run: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj --filter "FullyQualifiedName~ChatHubJoinTopicTests"`
Expected: PASS (2). If the build fails on the `ForumController` constructor wiring or a Hub property setter, fix minimally and report; if blocked, report the exact compiler error.

- [ ] **Step 7: Commit**

```bash
cd /d/src/lovecraft
git add Lovecraft/Lovecraft.Backend/Hubs/ChatHub.cs Lovecraft/Lovecraft.Backend/Controllers/V1/ForumController.cs Lovecraft/Lovecraft.Backend/Program.cs Lovecraft/Lovecraft.UnitTests/ChatHubJoinTopicTests.cs
git -c user.name="amorofrost" -c user.email="amorofrost@gmail.com" commit -m "fix: gate ChatHub.JoinTopic with IForumTopicAccess (closes topic-leak); controller delegates event-topic check"
```

---

### Task 3: Full regression run

**Files:** none.

- [ ] **Step 1: Run the whole backend test suite**

Run: `dotnet test Lovecraft.UnitTests/Lovecraft.UnitTests.csproj`
Expected: BUILD + all tests PASS — especially `AclTests`, `ForumTests`, `EventTopicAccessTests`, `ChatTests` (the controller refactor must not change their behavior), plus the new `ForumTopicAccessTests` (8) + `ChatHubJoinTopicTests` (2). If any pre-existing test regressed, the controller delegation changed behavior — investigate before proceeding.

---

## Self-review checklist (author)

- **Spec coverage (android spec §3.2):** extract shared `IForumTopicAccess` → Task 1; gate `JoinTopic` (throw `HubException`) → Task 2 Step 3; controller delegates event-topic check (single source of truth, no drift) → Task 2 Step 4; DI → Task 2 Step 5; regression test asserting `JoinTopic` denial for a non-attendee → `ChatHubJoinTopicTests` + the `ForumTopicAccessTests` access matrix.
- **Placeholder scan:** none — concrete code + exact commands.
- **Backward-compat:** the controller keeps its REST error codes (only the event-topic boolean is delegated, logic identical); legitimate clients only join topics they can already see, so no behavior change for them. The hub now denies what REST already denied.
- **Type consistency:** `IForumTopicAccess.CanViewEventTopicContentAsync(ClaimsPrincipal, ForumTopicDto)` used by the controller; `CanViewTopicAsync(ClaimsPrincipal, string)` used by the hub + tests; `ChatHub` ctor becomes `(IChatService, IPresenceTracker, IForumTopicAccess)` — matched by the hub test's `new ChatHub(...)`; `ForumTopicAccess.ResolveEventIdFromTopic` replaces the controller's deleted private copy.

## Follow-on / notes

- The web SignalR client is fixed by the same server change (it was equally exposed). No client change required.
- The Android client (separate repo) already only calls `JoinTopic` after a REST fetch (defense in depth); this server gate is the actual boundary.
