# Event and campaign invite codes

## Storage (Azure Table)

- Table: `eventinvites` (with optional `AZURE_TABLE_PREFIX`).
- Partition key: `INVITE`.
- Row key: **normalized plaintext code** (trim + uppercase). The code is stored readably for operational simplicity; protect Table Storage access accordingly.
- Columns include: `EventId`, `PlainCode` (duplicate of row key for explorers), `CampaignLabel`, `ExpiresAtUtc`, `Revoked`, `CreatedAtUtc`, `RegistrationCount`, `EventAttendanceClaimCount`.

## Event invites

- Tied to a real event id (e.g. seeded ids `1`, `2`, or generated `evt-…` from the admin API).
- **Create / rotate:** `POST /api/v1/admin/events/{eventId}/invites` with `expiresAtUtc` and optional `plainCode`. If `plainCode` is omitted, a readable code is generated. Previous non-revoked codes for that event are **revoked** first.
- **Validation:** `GET /api/v1/events/{id}?code=…` uses the code to unlock secret events for viewing.
- **Registration:** If `inviteCode` is supplied at signup and matches an invite, `RegistrationSourceEventId` is set and the user is registered for the event **unless** the invite is a campaign-only code (see below).
- **Counters:**
  - `RegistrationCount` — incremented when a **new account** is created using this code (after the user row is written).
  - `EventAttendanceClaimCount` — incremented when an **existing** user successfully `POST`s to `/api/v1/events/{id}/register` with body `{ "inviteCode": "…" }` and the code’s `EventId` matches that event.

## Campaign (non-event) invites

- `EventId` is a **negative integer string** (e.g. `-1`, `-2`). These ids do **not** refer to rows in the events table.
- Used for acquisition / marketing: signup still records `RegistrationSourceEventId` (e.g. `-1`), but **no** `RegisterForEvent` call is made for campaign codes.
- Multiple codes can share the same campaign id; creating a new code does **not** revoke siblings (unlike event rotate).
- **Create:** `POST /api/v1/admin/invites/campaigns` with `campaignId`, optional `campaignLabel`, `expiresAtUtc`, optional `plainCode` (otherwise auto-generated).

## Admin UI

- **Events → edit:** lists invite rows for that event with plaintext, expiry, counters, and revoked state.
- **Invites:** global list with links to events (or campaign badges), plus a form to create campaign invites.

## Site-wide registration policy

Unchanged: `appconfig` / registration partition `require_event_invite`. See [DOCKER.md](./DOCKER.md#invite-codes-and-registration).

## Legacy hashed rows

Older deployments may have row keys that are HMAC hashes. Those rows are not found by plaintext lookup. Re-seed or recreate invites after migrating to plaintext row keys.
