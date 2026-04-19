# Events — backend reference

This document describes **event-related behavior** in the Lovecraft API: visibility, registration, interest, free-text fields, and **event-linked forum topics** (including per-topic visibility).

**Related:** [INVITES.md](./INVITES.md) (invite codes, campaigns, counters).

---

## Data model (`EventDto` / `EventEntity`)

| Field | Notes |
|--------|--------|
| `id` | String row key. |
| `title`, `description`, `imageUrl`, `badgeImageUrl` | `description` may contain newlines; clients render with preserved line breaks. |
| `date`, `endDate`, `location`, `capacity` | Standard scheduling/venue. |
| `price` | **Free-text string** (e.g. `"2500 ₽"`, `"from $100"`). Not a numeric currency field. |
| `externalUrl` | Optional official site / tickets link. |
| `organizer` | Display string. |
| `category` | `EventCategory` enum (camelCase in JSON). |
| `visibility` | `public` \| `secretTeaser` \| `secretHidden`. |
| `isSecret` | Legacy: `true` when visibility is not public. |
| `attendees` | User IDs registered as **attending** (via invite + `POST .../register`, or staff). |
| `interestedUserIds` | Users who marked **interested** (separate from attendance). |
| `forumTopicId` | Primary **public** discussion topic id (`event-topic-{eventId}`) once created. |
| `archived` | When `true`, hidden from normal listings; admin can still manage. |

---

## Visibility

| Value | Listings / cards | Full detail API | Forum “summary” row (Talks) | Forum topics & replies |
|--------|------------------|-----------------|-----------------------------|-------------------------|
| **public** | Everyone | Everyone | Everyone authenticated | Per **topic** visibility (below) |
| **secretTeaser** | Teaser (redacted fields for non-invite users) | Full detail only with invite/unlock | Teaser viewers see discussion **card** | **Public** topics visible to anyone who can see the teaser; **attendees-only** / **specific users** follow rules below |
| **secretHidden** | Hidden unless attendee/staff | Hidden unless invite code / attendee / staff | Only if attendee or staff | Same per-topic rules |

Helpers live in `Lovecraft.Backend/Helpers/EventForumAccess.cs` (event-level) and `EventTopicAccess.cs` (per-topic).

---

## Registration vs interest

- **Attending** (`attendees`): `POST /api/v1/events/{id}/register` with `{ "inviteCode": "..." }` for non-staff. Staff may omit the code. **Unregister:** `DELETE /api/v1/events/{id}/register`.
- **Interested:** `POST /api/v1/events/{id}/interest` and `DELETE /api/v1/events/{id}/interest` — does not grant attendance or forum attendee-only access.

Invite validation for viewing secret events: `GET /api/v1/events/{id}?code=...` (see [INVITES.md](./INVITES.md)).

---

## Forum: event discussions (`sectionId`: `events`)

### Event-level rules (summary)

`EventForumAccess`:

- **Summary** (Talks → event discussion list): who may **see that an event has discussions** (card/row).
- Legacy **topics/replies** gate is superseded by **per-topic** visibility for listing and `GET topic` / replies (see `EventTopicAccess`).

### Per-topic visibility (`EventTopicVisibility`)

Stored on each `ForumTopicDto` / `ForumTopicEntity` for `events` section topics:

| Value | Who can see the topic (non–staff) |
|--------|-----------------------------------|
| **public** | Anyone who passes **event discussion summary** for that event (`CanViewEventDiscussionSummary`). |
| **attendeesOnly** | Users listed in `EventDto.attendees` only. |
| **specificUsers** | Users whose id is in `topic.allowedUserIds` (non-empty list). |

Staff (`moderator` / `admin`) bypass these checks.

JSON: `eventTopicVisibility` is a camelCase string (`public`, `attendeesOnly`, `specificUsers`); `allowedUserIds` is a string array.

### Auto-created topics

When `GET /api/v1/events/{id}` runs and `forumTopicId` is empty, the backend creates **two** topics:

1. **`event-topic-{eventId}`** — **public** main thread; `event.forumTopicId` points here.
2. **`event-attendees-{eventId}`** — **attendeesOnly** companion thread.

Admin may create additional topics and edit visibility via admin API.

### Admin API (forum subset)

- `GET /api/v1/admin/events/{eventId}/forum-topics` — list topics (elevated; sees all).
- `POST /api/v1/admin/events/{eventId}/forum-topics` — body includes `CreateTopicRequestDto` fields plus optional `eventTopicVisibility`, `allowedUserIds`.
- `PUT /api/v1/admin/forum-topics/{topicId}` — update including `eventTopicVisibility` / `allowedUserIds`.

### Public forum API

- `GET /api/v1/forum/event-discussions/summary` — events the user may see in Talks (with topic counts filtered by visible topics).
- `GET /api/v1/forum/event-discussions/{eventId}/topics` — topics for the event, **filtered** by `EventTopicAccess`.
- `GET /api/v1/forum/topics/{topicId}`, replies — allowed only if the user may view that topic.

---

## Main HTTP surface (events)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/v1/events` | List events (visibility-filtered). |
| GET | `/api/v1/events/{id}` | Detail; optional `?code=` for invites; may auto-create default forum topics. |
| POST | `/api/v1/events/{id}/register` | Attend (invite body for non-staff). |
| DELETE | `/api/v1/events/{id}/register` | Leave attendance. |
| POST | `/api/v1/events/{id}/interest` | Mark interested. |
| DELETE | `/api/v1/events/{id}/interest` | Remove interest. |

Admin event CRUD, attendees, invites, and forum topics are under `/api/v1/admin/...` (see Swagger).

---

## Key code locations

- `Lovecraft.Common/DTOs/Events/EventDtos.cs`, `Admin/AdminDtos.cs`
- `Lovecraft.Common/DTOs/Forum/ForumDtos.cs` — `ForumTopicDto`, `CreateTopicRequestDto`, `UpdateTopicRequestDto`
- `Lovecraft.Common/Enums/EventTopicVisibility.cs`
- `Lovecraft.Backend/Helpers/EventForumAccess.cs`, `EventTopicAccess.cs`
- `Lovecraft.Backend/Controllers/V1/EventsController.cs`, `ForumController.cs`, `AdminController.cs` (event/forum sections)
- `Lovecraft.Backend/Services/Azure/AzureEventService.cs`, `AzureForumService.cs` (+ mock/caching counterparts)
- `Lovecraft.Backend/Storage/Entities/EventEntity.cs`, `ForumTopicEntity.cs`

---

*Last updated: 2026-04-18*
