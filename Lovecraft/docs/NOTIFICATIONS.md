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
