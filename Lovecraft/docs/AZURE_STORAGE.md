# Azure Storage Schema

**LoveCraft Backend — data persistence layer**

**Last Updated**: 2026-05-15
**Storage types**: Azure Table Storage (relational-style data) + Azure Blob Storage (images)
**Mode switch**: `USE_AZURE_STORAGE=true|false` in env

---

## Overview

27 tables under `TableNames.Prefix + name`. The optional `AZURE_TABLE_PREFIX` env var (e.g. `dev_`, `test_`) lets staging and integration tests share an Azure Storage account without colliding. Both the backend and the `Lovecraft.Tools.Seeder` CLI respect the prefix.

Design principles:
- **Denormalisation over joins** — Table Storage has no JOIN; reverse-lookup indexes are separate tables
- **Partition-first queries** — point queries (PK + RK) are O(1); avoid table scans
- **Reverse-timestamp RowKeys** for streams that need "newest first" (messages, forum replies)

---

## Tables (27)

### Identity & auth

#### `users`
PK `user-{firstChar(userId)}` · RK `userId` (GUID)

Stores the full user profile. See [AUTHENTICATION.md](./AUTHENTICATION.md#-storage-schema) for the field list. Notable fields beyond name/age/etc.:

- `AuthMethodsJson` — list of `"local"`, `"google"`, `"telegram"` (any combination)
- `TelegramUserId`, `GoogleUserId` — non-empty when those providers are linked
- `Country`, `Region` — structured location (ISO 3166-1 alpha-2 country code + region string); `Location` retained as legacy free-text field
- `SecondaryCountry`, `SecondaryRegion` — optional secondary slot for users with two home regions; same shape and validation as primary
- `InstagramHandle` — optional public handle
- `EmailVerified`, `IsOnline`, `LastSeen`
- `StaffRole` (`"none"|"moderator"|"admin"`), `RankOverride` (admin-set; otherwise null and rank is computed)
- `ReplyCount`, `LikesReceived`, `EventsAttended`, `MatchCount` — counters feeding `RankCalculator`
- `RegistrationSourceEventId`, `RegistrationSourceRedeemedAtUtc` — immutable invite-source audit
- `PreferencesJson`, `SettingsJson`, `FavoriteSongJson`, `ImagesJson`, `PromptsJson`

#### `useremailindex`
PK `lowercased(email)` · RK `"INDEX"` · value `UserId`

Reverse lookup for email-based login.

#### `usertelegramindex`
PK `telegramUserId` (string) · RK `"INDEX"` · value `UserId`

Reverse lookup for `TelegramLoginAsync` / Mini App. Added when an account links Telegram.

#### `usergoogleindex`
PK `googleSubject` · RK `"INDEX"` · value `UserId`

Reverse lookup for Google sign-in. Added when an account links Google.

#### `refreshtokens`
PK `userId` · RK `tokenId`

Hashed refresh tokens with `ExpiresAt`, `RevokedAt`, `ReplacedByTokenId` (rotation chain).

#### `authtokens`
PK `EMAIL_VERIFY` or `PASSWORD_RESET` · RK `token`

Short-lived single-use tokens (24 h verify / 30 min reset).

### Events & invites

#### `events`
PK `"EVENTS"` (single partition — events are read together) · RK `eventId`

Notable fields: `Title`, `Description` (multi-line preserved), `ImageUrl`, `BadgeImageUrl`, `Date`, `EndDate`, `Location`, `Capacity`, `Category`, `Price` (free-text string, e.g. `"2500 ₽"`), `Organizer`, `ExternalUrl`, `IsSecret` (legacy bool), `Visibility` (`Public`|`SecretTeaser`|`SecretHidden`), `ForumTopicId` (primary public discussion topic id), `Archived`.

See [EVENTS.md](./EVENTS.md) for visibility semantics.

#### `eventattendees`
PK `eventId` · RK `userId`

Forward index of registered attendees.

#### `eventinterested`
PK `eventId` · RK `userId`

Tracks the "interested" flag separately from attendance. Does not grant attendee-only forum topic access.

#### `eventinvites`
PK `"INVITE"` · RK normalised plaintext code (uppercase, trimmed)

Readable codes (case-insensitive lookup). Fields: `EventId` (real event id, or **negative** integer string for campaign codes), `CampaignLabel`, `PlainCode` (duplicate of RowKey), `ExpiresAtUtc`, `Revoked`, `CreatedAtUtc`, `RegistrationCount`, `EventAttendanceClaimCount`.

See [INVITES.md](./INVITES.md).

### Matching

#### `likes`
PK `fromUserId` · RK `toUserId`

#### `likesreceived`
PK `toUserId` · RK `fromUserId`

Reverse index so "who liked me" is a partition scan rather than a table scan.

#### `matches`
PK `userId1` · RK `userId2` (lex-sorted)

Note: matches are also computed at query time as the intersection of `likes` and `likesreceived`. This table is denormalised metadata.

### Chats

#### `chats`
PK `"CHAT"` · RK `chatId`

`ParticipantIds` (comma-separated), `CreatedAt`.

#### `userchats`
PK `userId` · RK `chatId`

Per-user index for chat list. Fields: `OtherUserId`, `LastMessageContent` (truncated), `LastMessageAt`, `UnreadCount`, `UpdatedAt`.

#### `messages`
PK `chatId` · RK `{invertedTicks}_{messageId}` so newest messages sort first

`MessageId`, `SenderId`, `Content`, `SentAt`, `Type`, `Read`, image attachments via separate fields.

See [CHAT_ARCHITECTURE.md](./CHAT_ARCHITECTURE.md).

### Forum

#### `forumsections`
PK `"FORUM"` · RK `sectionId`

`Name`, `Description`, `OrderIndex`, `TopicCount`, `MinRank`.

#### `forumtopics`
PK `"SECTION#{sectionId}"` · RK `topicId`

`Title`, `Content`, `AuthorId`, `IsPinned`, `IsLocked`, `ReplyCount` (denormalised), `MinRank`, `NoviceVisible`, `NoviceCanReply`, `EventId` (when in `events` section), `EventTopicVisibility` (`public`|`attendeesOnly`|`specificUsers`), `AllowedUserIdsJson`.

#### `forumtopicindex`
PK `"TOPIC_INDEX"` · RK `topicId` · value `sectionId`

Lets `GET /forum/topics/{id}` look up the section partition without scanning.

#### `forumreplies`
PK `"TOPIC#{topicId}"` · RK `{invertedTicks}_{replyId}`

`ReplyId`, `AuthorId`, `Content`, `ImageUrlsJson`, `Likes`, `CreatedAt`.

### Store, blog, app config

#### `storeitems`
PK `STORE#{category}` · RK `itemId`

Catalog (read-mostly). `Title`, `Description`, `Price` (decimal), `ImageUrl`, `Category`, `StockQuantity`, `IsAvailable`, `ExternalPurchaseUrl`.

#### `blogposts`
PK `"BLOG"` · RK `{invertedTicks}_{postId}` (newest first)

`PostId`, `Title`, `Excerpt`, `Content`, `ImageUrl`, `AuthorId`, `TagsJson`, `PublishedAt`, `IsPublished`.

#### `appconfig`
PK is one of:
- `rank_thresholds` (10 integer rows: `active_replies`, `active_likes`, `active_events`, `friend_replies`, `friend_likes`, `friend_events`, `crew_replies`, `crew_likes`, `crew_events`, `crew_matches`)
- `permissions` (11 string rows: `create_topic`, `delete_own_reply`, `delete_any_reply`, `delete_any_topic`, `pin_topic`, `ban_user`, `assign_role`, `override_rank`, `manage_events`, `manage_blog`, `manage_store` → minimum required rank/role name)
- `registration` (`require_event_invite` → `true|false`)

RK is the config key, `Value` is the string-encoded value. Served by `AzureAppConfigService` with a 1-hour `IMemoryCache`. Fallback to `RankThresholds.Defaults` / `PermissionConfig.Defaults` on missing or invalid rows. Seeded by `Lovecraft.Tools.Seeder`.

### Notifications

#### `notifications`
PK `userId` (recipient) · RK `{invertedTicks}_{notificationId}`

Canonical record. Fields: `Type`, `ActorId?`, `PayloadJson`, `CreatedAtUtc`, `ReadAtUtc?`,
`DismissedAtUtc?`, `DigestGroupId?`, `SourceEventId?` (used by `NotificationDeduper`).

#### `notificationsoutbox`
PK partition naming: `OUTBOX_{channel}_PENDING` while pending; `OUTBOX_{channel}_DONE_{yyyy-MM-dd}`
after success; `OUTBOX_{channel}_DEAD_{yyyy-MM-dd}` after 5 failed attempts.
RK `{scheduledForUtc:yyyy-MM-ddTHH:mm:ss}_{notificationId}` (lex = chronological).

#### `notificationpreferences`
PK `userId` · RK `INDEX`. Fields: `MatrixJson`, `FrequencyJson`, `DailyDigestHourUtc`,
`Mute`, `MutedUntilUtc?`. Defaults loaded by `MockNotificationPreferenceService.BuildDefaults`.

#### `webpushsubscriptions`
PK `userId` · RK `deviceId`. Fields: `Endpoint`, `P256dh`, `Auth`, `UserAgent`,
`CreatedAtUtc`, `LastSeenAtUtc`. No consumer wired until Phase E.

---

## Blob Storage

Two public-read containers (currently — see TD.8 for SAS-token plan):

### `profile-images`
Naming: `{userId}/{guid}.jpg`

Profile photo uploads via `POST /api/v1/users/{id}/images` and external-CDN downloads from Google/Telegram. The GUID-per-upload pattern (added 2026-04-15) eliminates enumeration risk — old blobs are deleted on re-upload.

### `content-images`
Naming: `{userId}/{guid}.jpg`

Forum reply / chat message attachments via `POST /api/v1/images/upload`. Backend validates content-type (JPEG/PNG/GIF/WebP), enforces ≤10 MB, resizes to 1200 px max, re-encodes as JPEG Q85.

`AzureImageService` creates containers on first startup with `CreateIfNotExistsAsync(PublicAccessType.Blob)`.

---

## Query patterns

### Fast (use these)

**Point query** (PK + RK):

```csharp
var user = await tableClient.GetEntityAsync<UserEntity>(
    partitionKey: UserEntity.GetPartitionKey(userId),
    rowKey: userId);
```

~5–10 ms.

**Partition query** (single PK):

```csharp
var attendees = tableClient.QueryAsync<EventAttendeeEntity>(
    filter: $"PartitionKey eq '{eventId}'");
```

~10–50 ms depending on row count.

### Slow (avoid)

Table scans (no PK filter) — multi-second response on populated tables. Add a reverse-lookup index instead.

---

## Consistency patterns

### Eventually consistent

Counter updates retry up to 3× on ETag 412 and are wrapped in try/catch — counter failures **never** fail the primary operation.

### Like → match flow

```
1. Write to `likes`: PK=fromUserId, RK=toUserId
2. Write to `likesreceived`: PK=toUserId, RK=fromUserId
3. Check for reverse like (`likes` with PK=toUserId, RK=fromUserId)
4. If exists, create `matches` row (PK=min(userId), RK=max(userId)) AND auto-create a 1-on-1 chat via IChatService.GetOrCreateChatAsync
```

`GetOrCreateChatAsync` is idempotent — duplicate chats are never created.

---

## Cost estimates (Standard LRS, Hot tier)

| Component | 1,000 users | 10,000 users |
|---|---|---|
| Table storage | ~$0.005/mo | ~$0.05/mo |
| Blob storage (~500 KB avg profile image) | ~$0.03/mo | ~$0.30/mo |
| Transactions (lookups + writes) | negligible | ~$1/mo |

The dominant cost as the user base grows is blob storage. Switching to private blobs + SAS tokens (TD.8) adds no storage cost — just an auth check.

---

## Development tools

### Azurite (local emulator)

```bash
npm install -g azurite
azurite --location ./azurite-data
```

Set `AZURE_STORAGE_CONNECTION_STRING=UseDevelopmentStorage=true` in backend `.env`. Azurite emulates both Table and Blob services.

### Azure Storage Explorer

GUI for browsing tables and blobs: <https://azure.microsoft.com/features/storage-explorer/>.

### Seeder

```bash
dotnet run --project Lovecraft.Tools.Seeder
```

Populates all 23 tables from `MockDataStore`. Set `AZURE_TABLE_PREFIX` to seed into an isolated namespace.

---

## References

- [Azure Table Storage design guide](https://learn.microsoft.com/azure/cosmos-db/table-storage-design)
- [Azure Storage naming conventions](https://learn.microsoft.com/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata)
