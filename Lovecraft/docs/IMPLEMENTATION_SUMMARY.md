# Implementation Summary

## What's Built

A complete .NET 10 backend with REST API endpoints, JWT authentication, Azure Table Storage, SignalR real-time messaging, SendGrid email, and Google + Telegram identity providers. Full-stack deployed on Azure VM at `https://aloeve.club` behind Cloudflare (DNS proxy + DDoS protection) and nginx (TLS termination, HTTP→HTTPS redirect).

> See [AUTHENTICATION.md](./AUTHENTICATION.md) for the full auth surface (local + Google + Telegram), [TELEGRAM_AUTH.md](./TELEGRAM_AUTH.md) for the Telegram-specific flows, and [EVENTS.md](./EVENTS.md) for event visibility/invites/forum access. Last updated 2026-05-15.

### Solution Structure

```
Lovecraft.slnx
├── Lovecraft.Common/           # Shared library (DTOs, Enums, Models)
│   ├── DTOs/                  # Admin, Auth, Blog, Chats, Events, Forum, Images, Matching, Store, Users
│   ├── Enums/                 # All enumerations (incl. EventTopicVisibility, UserRank, StaffRole)
│   └── Models/                # ApiResponse<T>, PagedResult<T>
│
├── Lovecraft.Backend/         # ASP.NET Core Web API + SignalR
│   ├── Controllers/V1/        # Admin, Auth, Blog, Chats, Events, Forum, Images, Matching, Store, Users
│   ├── Auth/                  # JwtService, PasswordHasher, JwtSettings,
│   │                            TelegramLoginVerifier, TelegramWebAppVerifier, GoogleIdTokenVerifier
│   ├── Attributes/            # RequireStaffRoleAttribute (sync), RequirePermissionAttribute (async)
│   ├── Configuration/         # JwtSettings, TelegramAuthOptions, GoogleAuthOptions
│   ├── Helpers/               # RankCalculator, EffectiveLevel, PermissionGuard,
│   │                            EventForumAccess, EventTopicAccess, HtmlGuard, AppRuntime
│   ├── Services/              # IServices.cs + Mock*Service implementations (USE_AZURE_STORAGE=false)
│   │   ├── Azure/             # AzureAuthService, AzureUserService, AzureEventService,
│   │   │                        AzureMatchingService, AzureStoreService, AzureBlogService,
│   │   │                        AzureForumService, AzureChatService, AzureImageService,
│   │   │                        AzureAppConfigService, AzureEventInviteService
│   │   ├── Caching/           # UserCache (ConcurrentDictionary singleton),
│   │   │                        CachingEventService, CachingStoreService,
│   │   │                        CachingBlogService, CachingForumService
│   │   ├── Email/             # IEmailService, SendGridEmailService, NullEmailService
│   │   ├── EventInviteHelpers.cs, EventInviteNormalizer.cs
│   │   └── ...
│   ├── Storage/               # Azure Table Storage layer
│   │   ├── TableNames.cs      # 23 table-name properties; AZURE_TABLE_PREFIX support
│   │   └── Entities/          # 22 entity classes (incl. UserGoogleIndexEntity, UserTelegramIndexEntity,
│   │                            UserEmailIndexEntity, EventInviteEntity, EventInterestedEntity,
│   │                            AppConfigEntity, AuthTokenEntity)
│   ├── Hubs/ChatHub.cs        # SignalR hub (JoinChat/JoinTopic/SendMessage; JWT via query string)
│   ├── MockData/              # MockDataStore.cs — in-memory seed data
│   └── Program.cs             # Startup, DI mode switch, JWT, SignalR, rate limiting, CORS
│
├── Lovecraft.TelegramBot/     # Separate hosted-service worker (Telegram long-poll)
│   ├── Program.cs             # Host.CreateApplicationBuilder + AddHostedService<TelegramBotWorker>
│   └── TelegramBotWorker.cs
│
├── Lovecraft.Tools.Seeder/    # CLI: seeds Azure Tables from MockDataStore (respects AZURE_TABLE_PREFIX)
│
└── Lovecraft.UnitTests/       # xUnit — ~25 test classes (AuthenticationTests, AclTests,
                                  AppConfigServiceTests, CachingTests, ChatTests, EffectiveLevelTests,
                                  EmailServiceTests, EventInviteServiceTests, EventTopicAccessTests,
                                  ForumTests, GooglePendingFlowTests, HtmlGuardTests, ImageTests,
                                  MatchingTests, RankCalculatorTests, RateLimitingTests,
                                  RefreshTokenTests, ServiceTests, TelegramLoginVerifierTests,
                                  TelegramMiniAppFlowTests, TelegramPendingFlowTests,
                                  UserCacheTests, AzureUserServiceTests, UsersControllerUpdateTests)
```

### API Endpoints Implemented

All endpoints return data in the format:
```json
{
  "success": true/false,
  "data": { ... },
  "error": { ... },
  "timestamp": "..."
}
```

#### Users (`/api/v1/users`)
- `GET /api/v1/users` - List users (paginated)
- `GET /api/v1/users/{id}` - Get user by ID
- `PUT /api/v1/users/{id}` - Update user
- `PUT /api/v1/users/{id}/role` - Assign staff role. **Admin-only.**
- `PUT /api/v1/users/{id}/rank-override` - Override computed rank. **Admin-only.**

#### Events (`/api/v1/events`)
- `GET /api/v1/events` - List all events (filtered by `visibility`: public, secret teaser, secret hidden)
- `GET /api/v1/events/{id}` - Get event by ID; optional `?code=` invite; may auto-create default forum topics
- `POST /api/v1/events/{id}/register` - Register as attendee (invite code in body for non-staff)
- `DELETE /api/v1/events/{id}/register` - Unregister
- `POST` / `DELETE` `/api/v1/events/{id}/interest` - Interested flag (not attendance)

**Fields:** `price` is free text; `externalUrl` optional; `interestedUserIds` vs `attendees`; `forumTopicId` points at the primary public discussion thread.

**Forum:** Event-linked topics use per-topic visibility (`public`, `attendeesOnly`, `specificUsers`). See **[EVENTS.md](./EVENTS.md)** for rules and admin endpoints.

#### Matching (`/api/v1/matching`)
- `POST /api/v1/matching/likes` - Send a like
- `GET /api/v1/matching/likes/sent` - Get sent likes
- `GET /api/v1/matching/likes/received` - Get received likes
- `GET /api/v1/matching/matches` - Get matches

#### Store (`/api/v1/store`)
- `GET /api/v1/store` - List store items
- `GET /api/v1/store/{id}` - Get store item

#### Blog (`/api/v1/blog`)
- `GET /api/v1/blog` - List blog posts
- `GET /api/v1/blog/{id}` - Get blog post

#### Forum (`/api/v1/forum`)
- `GET /api/v1/forum/event-discussions/summary` - One row per event the user may see in Talks (includes filtered topic counts)
- `GET /api/v1/forum/event-discussions/{eventId}/topics` - Topics for that event (`sectionId` `events`), filtered by per-topic visibility — see **[EVENTS.md](./EVENTS.md)**
- `GET /api/v1/forum/sections` - List forum sections
- `GET /api/v1/forum/sections/{sectionId}/topics` - Get topics in section (not used for `events`; use event-discussions path)
- `POST /api/v1/forum/sections/{sectionId}/topics` - Create topic in section
- `GET /api/v1/forum/topics/{topicId}` - Get topic detail (title, content, author, pin status)
- `PUT /api/v1/forum/topics/{topicId}` - Update topic (author + moderator; `IsPinned`/`IsLocked` require moderator+). Returns `INSUFFICIENT_RANK` or `MODERATOR_REQUIRED` on rejection.
- `GET /api/v1/forum/topics/{topicId}/replies` - Get all replies for a topic
- `POST /api/v1/forum/topics/{topicId}/replies` - Post a reply (`{ content }` body)

#### Admin (`/api/v1/admin`)
- `GET /api/v1/admin/config` - Read `appconfig` values. **Admin-only.**
- Event CRUD, archive, attendees, per-event invites, and **event forum topics** (list/create/update/delete) — see Swagger and **[EVENTS.md](./EVENTS.md)**.

#### Chats (`/api/v1/chats`)
- `GET /api/v1/chats` - List user's chats
- `GET /api/v1/chats/{id}/messages` - Get paginated messages (`?page=1&pageSize=50`)
- `POST /api/v1/chats` - Get or create private chat (`{ targetUserId }` body)
- `POST /api/v1/chats/{id}/messages` - Send message via REST (`{ content }` body)

#### Image Upload (`/api/v1/images`)
- `POST /api/v1/images/upload` — multipart/form-data; validates content-type (JPEG/PNG/GIF/WebP) and size (≤10 MB); resizes to 1200px max, JPEG 85%; uploads to `content-images` Azure Blob container; returns `{ Url: string }`.

#### SignalR Hub (`/hubs/chat`)
- `JoinChat(chatId)` — join a private chat group (validates access)
- `JoinTopic(topicId)` — join a forum topic group (no auth check — public topics)
- `LeaveGroup(groupId)` — leave any group
- `SendMessage(chatId, content)` — validates access, persists message, broadcasts `MessageReceived` to `Clients.OthersInGroup` (sender excluded)
- Server → client events: `MessageReceived(message)`, `ReplyPosted(reply, topicId)`
- `MessageReceived` is also broadcast by `ChatsController.SendMessage` (REST path) via `IHubContext<ChatHub>.Clients.Group()` — both paths trigger real-time delivery
- Auth: JWT Bearer token passed as `?access_token=` query string

### DTOs Created

**User DTOs:**
- `UserDto` - User profile
- `UserPreferencesDto` - Matching preferences
- `UserSettingsDto` - User settings
- `AloeVeraSongDto` - Favorite song

**Event DTOs:**
- `EventDto` - Event details
- `EventRegistrationRequestDto` - Registration request

**Matching DTOs:**
- `LikeDto` - Like information
- `MatchDto` - Match information
- `CreateLikeRequestDto` - Like request
- `LikeResponseDto` - Like response with match info

**Store DTOs:**
- `StoreItemDto` - Store item

**Blog DTOs:**
- `BlogPostDto` - Blog post

**Forum DTOs:**
- `ForumSectionDto` - Forum section
- `ForumTopicDto` - Forum topic (includes `AuthorAvatar?`)
- `ForumReplyDto` - Forum reply (includes `Likes`, `AuthorAvatar?`, `imageUrls: string[]`)
- `CreateTopicRequestDto` - Create topic request
- `CreateReplyRequestDto` - Create reply request (`Content` property)

**Common Models:**
- `ApiResponse<T>` - Standard API response wrapper
- `ErrorResponse` - Error details
- `PagedResult<T>` - Paginated results

**Enums:**
- `Gender` - Male, Female, NonBinary, PreferNotToSay
- `EventCategory` - Concert, Meetup, Party, Festival, Yachting, Other
- `ChatType` - Private, Group
- `MessageType` - Text, Image, System
- `ProfileVisibility` - Public, Private, Friends
- `ShowMePreference` - Everyone, Men, Women, NonBinary
- `Language` - Ru, En

### Mock Data

`MockDataStore` (used when `USE_AZURE_STORAGE=false`) matches the frontend mock data in `aloevera-harmony-meet/src/data/`:
- 4 users (Anna, Dmitry, Elena, Maria) + the seeded `test@example.com`
- 10 events across all categories (Concert, Meetup, Festival, Yachting, Party, etc.) with mixed visibility (public + secret teaser + secret hidden)
- 4 store items
- 3 blog posts
- 4 forum sections (General, Music, Cities, Offtopic) with 12 topics + 25 replies
- 3 AloeVera songs

Mock storage also auto-seeds `MOCK-ATTEND-{eventId}` invite codes per event at startup so tests can register without admin steps.

### Enum Serialization

All C# enums are serialized as camelCase strings in JSON responses. Configured in `Program.cs`:
```csharp
builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
```
Examples: `EventCategory.Concert` → `"concert"`, `Gender.NonBinary` → `"nonBinary"`

### Configuration

**CORS:** `localhost:8080`, `localhost:5173`, `localhost:3000`, `aloeve.club`, `www.aloeve.club` (with credentials).

**Swagger:** OpenAPI 3 at `/swagger` (development environment only). Includes Authorize button for Bearer-token testing.

**Health Check:** `/health` is public; returns `{ status: "Healthy", timestamp, version, authentication }`.

### Docker

- `Dockerfile` — multi-stage build for the backend (build → publish → runtime on `mcr.microsoft.com/dotnet/aspnet:10.0`)
- `Dockerfile.telegram-bot` — image for the long-poll worker
- The production `docker-compose.yml` lives in the **frontend** repo (`aloevera-harmony-meet/`) and orchestrates three services: `frontend` (nginx + SPA), `backend`, and `telegram-bot`
- For backend-only local dev, this repo contains a simple `docker-compose.yml` that exposes the API on `http://localhost:5000`

### Documentation

See [README.md](../../README.md) for the full doc index. Key docs:
- **Auth**: `AUTHENTICATION.md`, `TELEGRAM_AUTH.md`, `GOOGLE_OAUTH_SETUP.md`
- **Storage**: `AZURE_STORAGE.md`
- **Real-time**: `CHAT_ARCHITECTURE.md`
- **Domain**: `EVENTS.md`, `INVITES.md`
- **Operations**: `DOCKER.md`, `QUICKSTART.md`, `API_TESTING.md`

### Testing

xUnit unit + integration tests in `Lovecraft.UnitTests/`. Run with `dotnet test`. Assembly-level `[CollectionBehavior(DisableTestParallelization = true)]` (in `AssemblyInfo.cs`) serialises the entire suite — pragmatic workaround for `MockDataStore` shared static state (tracked as `followup-mock-state-hygiene` for a proper migration off shared static state).

Coverage by file:

- **Auth core**: `AuthenticationTests`, `RefreshTokenTests`, `ServiceTests`
- **Identity providers**: `TelegramLoginVerifierTests`, `TelegramPendingFlowTests`, `TelegramMiniAppFlowTests`, `GooglePendingFlowTests`
- **Roles & ACL**: `AppConfigServiceTests` (cache hits/misses, 1-h TTL, fallback to `RankThresholds.Defaults`/`PermissionConfig.Defaults`, `LogWarning` on parse failure), `EffectiveLevelTests` (unified 0–5 map: Novice=0, ActiveMember=1, FriendOfAloe=2, AloeCrew=3, Moderator=4, Admin=5; `For(user, computedRank) = Math.Max(rankLevel, staffLevel)`), `RankCalculatorTests` (top-down OR-matching crew→novice, `RankOverride`, threshold boundaries), `AclTests` (integration via `WebApplicationFactory<Program>` + custom `TestAuthHandler`; `section.MinRank`, `topic.MinRank`, `topic.NoviceVisible`, `topic.NoviceCanReply` rejections returning `INSUFFICIENT_RANK` / `MODERATOR_REQUIRED` / `ADMIN_REQUIRED`)
- **Storage & caching**: `UserCacheTests` (`Set`/`Get`/`Remove`/`GetAll`, snapshot independence, 300-element concurrent writes, `LoadAsync`), `AzureUserServiceTests` (counter increments retry 3× on ETag 412; cache hit vs miss; write paths update cache entry), `CachingTests`
- **Domain**: `ChatTests`, `MatchingTests`, `ForumTests`, `EventTopicAccessTests`, `EventInviteServiceTests`, `ImageTests`, `UsersControllerUpdateTests`
- **Security**: `HtmlGuardTests`, `RateLimitingTests`
- **Infrastructure**: `EmailServiceTests`

## How to Use

### 1. With Docker (Recommended)

```bash
cd Lovecraft
docker-compose up --build
```

Access at: http://localhost:5000

### 2. With .NET CLI

```bash
cd Lovecraft
dotnet build
cd Lovecraft.Backend
dotnet run
```

### 3. Run Tests

```bash
cd Lovecraft
dotnet test
```

## What's Next

### Done since the original plan
- ✅ JWT authentication
- ✅ Azure Table Storage (23 tables, 11 Azure services, `Lovecraft.Tools.Seeder`)
- ✅ Chat endpoints (REST + SignalR `/hubs/chat`)
- ✅ Azure Blob Storage (`profile-images`, `content-images`, 1200px resize + JPEG 85%)
- ✅ Email delivery (SendGrid + `NullEmailService` fallback)
- ✅ Rate limiting (sliding window, 20 req/min/IP, shared bucket)
- ✅ Google sign-in (Google Identity Services ID token verification, pending-ticket flow)
- ✅ Telegram Login Widget + Telegram Mini App (Lovecraft.TelegramBot worker also shipped)
- ✅ Roles & ACL (rank thresholds + permissions in `appconfig`, `[RequireStaffRole]` + `[RequirePermission]`)
- ✅ Event invites + campaign invites (`eventinvites` table, admin API)
- ✅ HTTPS via Cloudflare + Origin Certificate (deployed at https://aloeve.club)
- ✅ User-visible error handling, form validation, profile image upload, BB code, image attachments, SEO metadata

### Still open
1. Songs backend endpoint (frontend `songsApi.ts` still mock-only)
2. Azure Blob SAS tokens for private blobs (currently public-read; profile blobs use `{userId}/{guid}.jpg` to avoid enumeration)
3. Account lockout after failed logins
4. SignalR enhancements: online presence, typing indicators, unread push updates
5. Notifications (in-app + push)
6. Pagination on list views (server-side `PagedResult<T>` exists; client-side wiring pending)
7. Application Insights / structured logging
8. Admin panel content removal / moderation queue / user blocking
9. Telegram Mini App polish (deep-link start params, command menu, full inline wizard)
10. aloeband.ru scraper for events + store items

## Notes (Current State)

- JWT authentication fully operational; all content endpoints require `[Authorize]`
- **Azure Table Storage** active when `USE_AZURE_STORAGE=true` in `.env`; falls back to in-memory mock services when false
- **`UserCache`** — `Services/Caching/UserCache.cs`; a `ConcurrentDictionary<string, UserEntity>` singleton registered in DI. `Program.cs` calls `LoadAsync(TableClient)` on startup (Azure mode only) to populate it with every row from the `users` table before the first request is served. `AzureUserService.GetUsersAsync` and `GetUserByIdAsync` read from the cache; all five write methods (`UpdateUser`, `IncrementCounter`, `SetStaffRole`, `SetRankOverride`, and `AzureAuthService` user-creation paths) call `_cache.Set(entity)` after each successful Azure write. `GetUsersAsync` Fisher-Yates shuffles the cache snapshot before applying skip/take so the swipe deck order is random per request. `MockUserService.GetUsersAsync` also shuffles.
- **`appconfig` table** (new in the Roles & ACL spec) — partitions: `rank_thresholds` (10 integer rows) and `permissions` (11 string rows). Served by `AzureAppConfigService` with 1-hour `IMemoryCache`; falls back to `RankThresholds.Defaults` / `PermissionConfig.Defaults` with `LogWarning` on missing/invalid rows. Seeded by `Lovecraft.Tools.Seeder`.
- **`IAppConfigService`** — singleton; 1-hour cached read of the `appconfig` table; fallback to code-defined defaults for missing rows. `IUserService` gained `IncrementCounterAsync` / `SetStaffRoleAsync` / `SetRankOverrideAsync`; counter increments in the Azure implementation retry 3× on ETag 412 and are wrapped in try/catch so counter failures never fail the primary operation. `IForumService` gained `UpdateTopicAsync`.
- **ACL enforcement**: `PermissionGuard.MeetsAsync(user, userService, requiredLevel)` is the shared helper; `[RequireStaffRole("moderator"|"admin")]` is a synchronous filter reading only the `staffRole` JWT claim; `[RequirePermission("<key>")]` is an `IFilterFactory` that resolves the required level from `AppConfig.Permissions` at runtime and delegates to `PermissionGuard`. Error codes: `INSUFFICIENT_RANK`, `MODERATOR_REQUIRED`, `ADMIN_REQUIRED`. JWT access tokens now embed `staffRole` as a custom claim.
- **SignalR** at `/hubs/chat` — JWT passed as `?access_token=` query string; nginx `/hubs/` location block proxies WebSocket upgrade
- **Matching**: all controllers extract the caller's ID via `User.FindFirst(ClaimTypes.NameIdentifier)` — `"current-user"` placeholder is gone. Matches are computed at query time as the intersection of the `likes` and `likesreceived` tables; there is no dedicated `matches` table. A 1-on-1 chat is auto-created when a mutual like is detected (both mock and Azure paths).
- **Chat tables**: `chats` (PK="CHAT"), `userchats` (PK=userId index), `messages` (PK=chatId, RK=invertedTicks_{id}); tables are created by `AzureChatService` constructor via `CreateIfNotExistsAsync()` on first startup
- **Real-time delivery**: `ChatsController.SendMessage` broadcasts `MessageReceived` to `IHubContext<ChatHub>.Clients.Group($"chat-{id}")` after persisting each REST message, ensuring recipients receive live updates without using the hub's `SendMessage` method directly
- **Image uploads**: `MessageDto` and `ForumReplyDto` now carry `imageUrls: string[]` arrays. New endpoint `POST /api/v1/images/upload` validates content-type (JPEG/PNG/GIF/WebP) and size (≤10 MB), resizes to 1200px max (JPEG 85% quality), uploads to Azure Blob `content-images` container
- **External profile photo download**: When a user registers or links their account via Telegram or Google, `AzureAuthService` calls `IImageService.DownloadAndUploadExternalImageAsync` to fetch the provider's CDN photo, resize it (max 800px, JPEG Q85), and store it in the `profile-images` Azure Blob container. The resulting blob URL is saved to `UserEntity.ProfileImage`. This is best-effort — if the download fails the user's profile image is left empty rather than blocking registration. `MockAuthService` passes the external URL through unchanged.
- **Table prefix**: set `AZURE_TABLE_PREFIX` env var (e.g. `dev_`) to use isolated table sets for separate test datasets; respected by both the backend and the Seeder tool
- **Seeder**: run `dotnet run --project Lovecraft.Tools.Seeder` from `Lovecraft/` to populate users, events, store, blog, forum, and like edges (sent, received, and mutual scenarios). Set `AZURE_TABLE_PREFIX` to seed into a prefixed namespace.
- Test credentials after seeding: `test@example.com` / `Test123!@#`; mock users `user1@mock.local`–`user4@mock.local` / `Seed123!@#`
- CORS allows `localhost:8080`, `localhost:5173`, `localhost:3000`, `aloeve.club`, `www.aloeve.club`
- Access token: 15 min; Refresh token: 7 days (HttpOnly cookie + body-based fallback)
- Enums serialize as camelCase strings in all API responses
- **Rate limiting**: sliding window 20 req/min/IP, shared bucket across all `[EnableRateLimiting("AuthRateLimit")]` endpoints (login, register, forgot-password, reset, telegram/google variants); returns 429 + `Retry-After: 60`. `refresh` and `logout` are intentionally NOT rate-limited.
- **Email**: `SendGridEmailService` when `SENDGRID_API_KEY` is set, otherwise `NullEmailService` logs verification + reset links to console.
- **Deployed on Azure VM** at `https://aloeve.club`: nginx in the frontend container terminates TLS (Cloudflare Origin Certificate), proxies `/api/`, `/hubs/`, and `/swagger` to backend over Docker internal network; only ports 80 + 443 are public-exposed.
- **Telegram Bot worker** (`Lovecraft.TelegramBot`) runs as a separate hosted-service container (`telegram-bot` in docker-compose, image built from `Dockerfile.telegram-bot`). Long-polls Telegram for incoming messages — currently a scaffold for future deep-link / linking workflows.
