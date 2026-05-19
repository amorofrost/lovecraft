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

---

## Phase D scope (shipped 2026-05-18)

**Real Telegram dispatcher** lands in `Lovecraft.NotificationsWorker`:
- `TelegramDispatcher` reads user's `TelegramUserId` from the `users` Azure Table via a minimal `UserTelegramContactEntity` (worker now consumes 4 tables: notifications, notificationsoutbox, notificationpreferences, users)
- `TelegramMessageRenderer` produces per-type HTML body + inline keyboard
- `TelegramRateLimiter` enforces global concurrency cap (25) + per-chat 1-second cooldown
- Inline keyboard: `[Open in app]` (https://aloeve.club deep link) + `[Mute these]` (callback_data `mute:{typeCamelCase}`)
- Errors: 403 (bot blocked) → PermanentError dead-letter (no auto-disable of prefs in Phase D — see follow-up); other 4xx → PermanentError; network/timeout → RetryableError

**Mute callback flow** in `Lovecraft.TelegramBot`:
- `NotificationCallbackHandler` receives `mute:{type}` callbacks, POSTs to backend `/api/v1/internal/notifications/mute-type` with `X-Service-Token` header
- Backend `[RequireServiceToken]` action filter validates the token in constant-time; new `InternalController` resolves Telegram id → app user id via `usertelegramindex` table, calls `INotificationPreferenceService.SetChannelDisabledForTypeAsync` to flip the matrix cell

**Required env vars (Phase D additions):**
```
TELEGRAM_BOT_TOKEN=...                 # already used by Lovecraft.TelegramBot
INTERNAL_SERVICE_TOKEN=...             # shared secret backend ↔ bot ↔ (future) worker
BACKEND_INTERNAL_URL=http://backend:8080   # optional; defaults shown
```

**Known limitations:**
- English-only message text (no `Settings.Language` lookup yet)
- Actor names not resolved — notifications render with `Someone` or fall back to payload fields. Producer-side denormalization of actor display name into `NotificationModel.ActorName` is a follow-up.
- Bot-blocked (403) does not auto-clear `prefs.matrix.*.telegram = false` — would need a worker→backend back-channel call. Currently just dead-letters; user keeps receiving failed-dispatch attempts on subsequent notifications until they manually toggle off.

---

## Phase E scope (shipped 2026-05-18)

**Web Push** is a real delivery channel. Architecture: dispatcher lives in `Lovecraft.Backend` (the API process), NOT in `Lovecraft.NotificationsWorker`. Producer dispatches in-process via `IWebPushDispatcher` for `WebPush` channel, same pattern as `IInAppDispatcher` for SignalR.

**Setup:**
- `Lovecraft.Tools.VapidKeygen` CLI: `dotnet run --project Lovecraft.Tools.VapidKeygen` prints a fresh keypair. Copy `VAPID_PUBLIC_KEY` / `VAPID_PRIVATE_KEY` / `VAPID_SUBJECT` into `.env`. Run once per environment; rotation invalidates all subscriptions.
- `GET /api/v1/push/vapid-public-key` (no auth) exposes the public key to the frontend.

**Pipeline:**
- `WebPushPayloadRenderer` maps `NotificationDto` → `WebPushNotificationDto` (title, body, url). Same URL-allowlist treatment as Telegram for `CommunityBroadcast`.
- `WebPushDispatcher` iterates `IPushSubscriptionService.ListAsync(userId)`, sends each subscription via `WebPushClient.SendNotificationAsync(subscription, payload, vapidDetails)`. Dead subscriptions (HTTP 404/410) → call `_pushService.UnsubscribeAsync(userId, deviceId)`. Other errors → log + continue.

**In-process channel orphan fix:** `NotificationProducer` no longer enqueues `OUTBOX_InApp_PENDING` or `OUTBOX_WebPush_PENDING` rows — those channels are dispatched in-process and the outbox rows would be orphaned. The DispatcherWorker / DigestWorker / JanitorWorker only handle Telegram and Email.

**Frontend:**
- `public/sw.js` service worker — minimal `push` + `notificationclick` handlers
- `src/lib/webPush.ts` — `isWebPushSupported`, `getSubscriptionStatus`, `enableWebPush`, `disableWebPush` helpers
- `src/components/settings/NotificationPreferences.tsx` Web Push channel block has an "Enable on this device" button (or "Disable" if subscribed)
- Permission is requested on the explicit Enable click (browser requirement for user gesture)

**Required env vars (Phase E additions):**
```
VAPID_PUBLIC_KEY=...     # P-256 public key, base64url
VAPID_PRIVATE_KEY=...    # P-256 private key, base64url
VAPID_SUBJECT=mailto:noreply@aloeband.ru
```
Generate via `dotnet run --project Lovecraft.Tools.VapidKeygen`.

**Known follow-ups:**
- English-only payload text (no `Settings.Language` lookup yet — same trade-off as Phase D Telegram).
- `AppBaseUrl` hardcoded `/` paths in the renderer (no scheme/host — the URL is the path-only string; frontend's service worker prepends origin). Sufficient because notifications are origin-scoped to the user's subscribed app.

---

## Phase F scope (shipped 2026-05-18)

**Real email digests via SendGrid.** Architecture: producer writes outbox rows for `Email` channel (async handled by worker, not in-process like Web Push). Worker's `DigestWorker` aggregates daily digest rows per user at the scheduled hour. Real `EmailDispatcher` and `EmailDigestRenderer` dispatch via SendGrid.

**Setup:**
- `SendGridEmailService` was already integrated in Phase B (backend public endpoints). Phase F adds the digest rendering + worker dispatch path.
- Set `SENDGRID_API_KEY` in `.env`. If absent, `NullEmailService` logs the email to console.
- `FROM_EMAIL` env var specifies the sender address (e.g. `noreply@aloeband.ru`).

**Pipeline:**
- `EmailDispatcher` in `Lovecraft.NotificationsWorker` reads user's email from the `users` table.
- `EmailDigestRenderer` maps `DigestModel` (list of `NotificationModel` grouped by type) → HTML email body. Subject: `"AloeVera News Digest — {date}"` (English; no i18n yet). Includes a signed unsubscribe link.
- Unsubscribe flow: `UnsubscribeToken` helper generates HMAC-SHA256 signed `{userId}_{timestamp}_{digest}` tokens. Frontend or email click routes to `POST /api/v1/notifications/unsubscribe` (public, no auth) with token + channel. Token validity is 7 days; signature validated in-process.
- Errors: 4xx → PermanentError (dead-letter); network/timeout → RetryableError (retry backoff).

**`DigestProcessor` path change (Phase F):**
- Producer was calling `DigestProcessor.CreateDigestAsync(userId, channel, notificationIds)` which returned a list of `NotificationModel`.
- Phase F changes this: producer now passes the full `DigestModel` (pre-aggregated by type) directly to the dispatcher. `IEmailDispatcher.DispatchAsync(user, digest)` signature.
- Summary rendering logic moves into `EmailDigestRenderer.RenderAsync(digest) → html`.

**Required env vars (Phase F additions):**
```
SENDGRID_API_KEY=...              # SendGrid API key for email delivery
FROM_EMAIL=noreply@aloeband.ru    # Sender email address
FRONTEND_BASE_URL=https://aloeve.club  # Used in unsubscribe links
JWT_SECRET_KEY=...                # HMAC key for unsubscribe token signing
```

**Follow-ups:**
- Localization: render digest title and section headers in user's `Settings.Language` (currently English-only).
- Unsubscribe token expiry: 7 days is hardcoded; consider making it configurable.
- Digest template: currently plain HTML; consider styled HTML templates or Handlebars.
- Test email delivery: `dotnet test Lovecraft.UnitTests` includes `EmailServiceTests` and digest rendering tests.
