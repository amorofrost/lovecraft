# Notifications (backend)

**Last updated:** 2026-05-17
**Phase:** A (Foundations) shipped. Phases B–H pending — see spec.

## Architecture

Two-process split: producers in the API process write `notifications` + `notificationsoutbox`
rows when triggers fire. A separate `Lovecraft.NotificationsWorker` (Phase C+) drains the
outbox for Telegram and email; in-app and Web Push are dispatched directly from the API.

## Phase A scope (this phase)

- 4 Azure Tables: `notifications`, `notificationsoutbox`, `notificationpreferences`,
  `webpushsubscriptions`
- Enums: `NotificationType` (9 values), `NotificationChannel` (4), `NotificationFrequency` (3)
- Services: `INotificationService`, `INotificationPreferenceService`, `IPushSubscriptionService`
- Helpers: `NotificationPolicy.ResolveChannels`, `NotificationDeduper`, `IPresenceTracker`
- `IInAppDispatcher` (wraps `IHubContext<ChatHub>` for `NotificationReceived`)
- `INotificationProducer` facade — not yet wired to any call site (Phase B)
- `NotificationsController` — list/read/dismiss/preferences/push-subscribe endpoints

## What Phase A does NOT include

- Producer call-site wiring (Phase B)
- Frontend bell UI / settings (Phase B)
- Worker process (Phase C)
- Telegram delivery (Phase D)
- Web Push delivery (Phase E)
- Email digests (Phase F)
- Event reminders + admin broadcast (Phase G)
- Rank-up notifications (Phase H)

See [`aloevera-harmony-meet/docs/superpowers/specs/2026-05-17-notifications-design.md`](../../../aloevera-harmony-meet/docs/superpowers/specs/2026-05-17-notifications-design.md).

---

## Phase B scope (shipped 2026-05-18)

**Backend**

- 4 producer call sites wired to `INotificationProducer`:
  - `MatchingService.CreateLikeAsync` → `LikeReceived`
  - `MatchingService.GetMatchesAsync` (mutual like detection) → `MatchCreated`
  - `ChatsController.SendMessage` → `MessageReceived`
  - `ForumService.CreateReplyAsync` → `ForumReplyToThread` (broadcasts to all previous reply authors + thread creator)
- New endpoint: `GET /api/v1/notifications/availability` → `{ telegramLinked: bool, emailVerified: bool, webPushSubscribed: bool }` (reads current user's `TelegramUserId`, `EmailVerified`, and subscription count)
- **Known limitation** (not yet handled): Anonymous likes (MCF.8) are not tracked as notifications; only non-anonymous likes trigger `LikeReceived`

**Frontend**

- `notificationsApi.ts` — wraps `GET /api/v1/notifications/availability`
- `useNotificationSignalR` hook — pipes `NotificationReceived` events from the hub into the store
- `useNotificationStore` (Zustand) — in-memory notification state with actions: `addNotification`, `markRead`, `markAllRead`, `dismiss`
- `<NotificationBell>` component — header bell icon with unread count bubble
- `<NotificationDropdown>` — popover showing recent notifications with action buttons
- `/notifications` page — full notification list with filtering (read/unread/type)
- Notification preferences UI in `SettingsPage` — per-type frequency (immediate/daily) + channel toggles (read-only for in-app; future: Telegram/email/Web Push)
- Conservative defaults: in-app only, immediate frequency (Telegram/email/Web Push pending)
- **What is NOT shipped**: Backend worker (Phase C), Telegram delivery (Phase D), Web Push delivery (Phase E), email digests (Phase F)

---

## Still not wired

- `EventPublished`, `EventInviteReceived`, `CommunityBroadcast` (Phase G)
- `EventReminder` (worker, Phase G)
- `RankUp` (Phase H)

## API endpoints (Phase A)

| Method | Path | Description |
|---|---|---|
| GET | `/api/v1/notifications?cursor=&limit=20` | Paginated list (newest first) |
| GET | `/api/v1/notifications/unread-count` | `{ count: int }` |
| POST | `/api/v1/notifications/{id}/read` | Mark one read |
| POST | `/api/v1/notifications/mark-all-read` | Bulk mark |
| DELETE | `/api/v1/notifications/{id}` | Dismiss |
| GET | `/api/v1/notifications/preferences` | Get prefs (returns defaults if none stored) |
| PUT | `/api/v1/notifications/preferences` | Replace prefs (validator forces `inApp=true` per type, `inApp`+`webPush` frequency=immediate) |
| POST | `/api/v1/push/subscribe` | Register a Web Push subscription (no consumer wired yet — Phase E) |
| DELETE | `/api/v1/push/subscribe/{deviceId}` | Unsubscribe one device |

All require `Authorization: Bearer <token>`.

## Default preferences

All channels off except in-app. In-app frequency is immediate. Telegram/Web Push frequency
defaults to immediate; email defaults to daily. `DailyDigestHourUtc` defaults to 9.

---

## Phase C scope (shipped 2026-05-18)

**Worker container:** new `Lovecraft.NotificationsWorker` project alongside `Lovecraft.TelegramBot`. Runs three `BackgroundService` loops:

- **DispatcherWorker** (10s tick): drains `OUTBOX_{channel}_PENDING` rows whose `RowKey <= now` and `Frequency = Immediate`. Dispatches via `ITelegramDispatcher` or `IEmailDispatcher` (stubs in Phase C — log + return Delivered). Moves rows to `OUTBOX_{channel}_DONE_{date}` on success, `*_DEAD_{date}` after 5 retryable failures or 1 permanent failure. Retry backoff `{ 30s, 2m, 10m, 1h, 6h }`.
- **DigestWorker** (top-of-hour): aggregates `Frequency in (Hourly, Daily)` rows per user × channel. Daily rows are only dispatched when `now.Hour == user.DailyDigestHourUtc`. One dispatch per (user, channel) group regardless of member count.
- **JanitorWorker** (3am UTC daily): deletes `OUTBOX_*_DONE_*` / `*_DEAD_*` partitions older than 30 days; deletes `notifications` rows older than 90 days.

**Stub dispatchers:** `StubTelegramDispatcher` and `StubEmailDispatcher` log `"would dispatch X to user Y"` and return success. Replaced by real implementations in Phase D (Telegram.Bot SendMessage + inline keyboard) and Phase F (SendGrid digest renderer + signed unsubscribe links).

**Web Push is NOT in the worker** — it's dispatched in-process from `Lovecraft.Backend` (Phase E adds the dispatcher and VAPID config; outbox rows for `webPush` are written but the worker ignores them per channel filtering).

**Mock mode:** worker only runs when `USE_AZURE_STORAGE=true`. Local dev with `USE_AZURE_STORAGE=false` skips the worker entirely (backend runs in-process with all mock storage).

**Entity duplication:** `NotificationEntity`, `NotificationOutboxEntity`, `NotificationPreferencesEntity` are duplicated under `Lovecraft.NotificationsWorker/Entities/`. Keep in sync with `Lovecraft.Backend/Storage/Entities/`. Helpers like `PendingPartition()`, `DonePartition()`, `DeadPartition()`, `GetRowKey()` are duplicated.

**Outbox lifecycle illustrated:**
```
[producer writes]                [worker drains]
PENDING (Immediate, ready) ───►  DONE_{date}      (success)
                          ───►  DEAD_{date}      (5 retryable / 1 permanent)
                          ───►  PENDING (rescheduled, attempts+1)  (retryable)

[digest worker, top of hour]
PENDING (Hourly)        ───►  DONE_{date}        (aggregated, one dispatch per (user, channel))
PENDING (Daily)         ───►  DONE_{date}        (only when now.Hour == prefs.DailyDigestHourUtc)
```

**Required env vars (worker container):**
```
USE_AZURE_STORAGE=true
AZURE_STORAGE_CONNECTION_STRING=...
AZURE_TABLE_PREFIX=                  # optional; mirrors backend's prefix
```
(No JWT, no SendGrid, no Telegram bot token in Phase C — stubs require nothing. Phases D and F add their respective configs.)
