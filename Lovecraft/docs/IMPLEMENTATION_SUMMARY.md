# Implementation Summary

## What Was Created

A complete .NET 10 backend with REST API endpoints, JWT authentication, and Azure Table Storage. Full-stack deployed on Azure VM at `https://aloeve.club` behind Cloudflare (DNS proxy + DDoS protection) and nginx (TLS termination, HTTP→HTTPS redirect).

> This document covers the full backend implementation. JWT auth, Azure Table Storage, Docker + nginx deployment, HTTPS via Cloudflare Origin Certificate, and all content API endpoints are implemented and operational. See [AUTH_IMPLEMENTATION.md](./AUTH_IMPLEMENTATION.md) for auth details. For **events** (visibility, invites, forum), see [EVENTS.md](./EVENTS.md). Last updated April 28, 2026.

### Solution Structure

```
Lovecraft.slnx
├── Lovecraft.Common/           # Shared library (DTOs, Enums, Models)
│   ├── DTOs/                  # Auth, Blog, Chats, Events, Forum, Matching, Store, Users
│   ├── Enums/                 # All enumerations
│   └── Models/                # ApiResponse<T>
│
├── Lovecraft.Backend/         # ASP.NET Core Web API
│   ├── Controllers/V1/        # REST API controllers (Auth, Users, Events, Matching, Store, Blog, Forum)
│   ├── Services/              # IServices.cs + Mock*Service + Azure/*Service implementations
│   │   ├── MockUserService.cs, MockEventService.cs, ...  (USE_AZURE_STORAGE=false)
│   │   ├── Azure/AzureAuthService.cs, AzureUserService.cs, ...  (USE_AZURE_STORAGE=true)
│   │   └── Caching/           # In-process caching layer
│   │       ├── UserCache.cs   # ConcurrentDictionary<string,UserEntity> — populated from Azure on startup; updated on every write
│   │       ├── CachingEventService.cs, CachingForumService.cs, ...  (IMemoryCache wrappers)
│   ├── Storage/               # Azure Table Storage layer
│   │   ├── TableNames.cs      # 18 table name properties; optional AZURE_TABLE_PREFIX for isolated datasets
│   │   └── Entities/          # 17 entity classes (UserEntity, EventEntity, ChatEntity, etc.)
│   ├── Hubs/                  # SignalR hubs
│   │   └── ChatHub.cs         # Real-time chat hub (JWT auth, JoinChat/JoinTopic/SendMessage)
│   ├── MockData/              # MockDataStore.cs — in-memory seed data
│   └── Program.cs             # Startup, DI, mode switch (USE_AZURE_STORAGE), SignalR
│
├── Lovecraft.Tools.Seeder/    # CLI tool: seeds Azure Table Storage from MockDataStore
│   └── Program.cs             # Reads .env, seeds users/events/store/blog/forum + like edges; respects AZURE_TABLE_PREFIX
│
└── Lovecraft.UnitTests/       # xUnit tests (264 tests)
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
- `GET /api/v1/forum/event-discussions/{eventId}/topics` - Topics for that event (`?page=1`; returns `PagedResult<ForumTopicDto>`), filtered by per-topic visibility — see **[EVENTS.md](./EVENTS.md)**
- `GET /api/v1/forum/sections` - List forum sections
- `GET /api/v1/forum/sections/{sectionId}/topics` - Get topics in section (`?page=1`; returns `PagedResult<ForumTopicDto>`; pinned-first then `UpdatedAt` desc)
- `POST /api/v1/forum/sections/{sectionId}/topics` - Create topic in section
- `GET /api/v1/forum/topics/{topicId}` - Get topic detail (title, content, author, pin status)
- `PUT /api/v1/forum/topics/{topicId}` - Update topic (author + moderator; `IsPinned`/`IsLocked` require moderator+). Returns `INSUFFICIENT_RANK` or `MODERATOR_REQUIRED` on rejection.
- `GET /api/v1/forum/topics/{topicId}/replies` - Get cursor-paginated replies (`?cursor=<rowKey>`; returns `PagedResult<ForumReplyDto>` newest-first)
- `POST /api/v1/forum/topics/{topicId}/replies` - Post a reply (`{ content }` body)

#### Admin (`/api/v1/admin`)
- `GET /api/v1/admin/config` - Read `appconfig` values. **Admin-only.**
- Event CRUD, archive, attendees, per-event invites, and **event forum topics** (list/create/update/delete) — see Swagger and **[EVENTS.md](./EVENTS.md)**.

#### Chats (`/api/v1/chats`)
- `GET /api/v1/chats` - List user's chats
- `GET /api/v1/chats/{id}/messages` - Get cursor-paginated messages (`?cursor=<rowKey>`; returns `PagedResult<MessageDto>` newest-first)
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

Matches the frontend mock data (centralized in `src/data/` on the frontend):
- 4 Users (Anna, Dmitry, Elena, Maria)
- 10 Events (Concert, Meetup, Festival, Yachting, etc.)
- 4 Store Items (T-shirt, Vinyl, Poster, Hoodie)
- 3 Blog Posts
- 4 Forum Sections with 12 Topics (General, Music, Cities, Offtopic) and 25 Replies
- 3 AloeVera Songs

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

**CORS:** Configured for frontend (localhost:8080, localhost:5173)

**Swagger:** Full OpenAPI documentation at `/swagger`

**Health Check:** `/health` endpoint

### Docker Support

- `Dockerfile` - Multi-stage build (build → publish → runtime)
- `docker-compose.yml` - Simple compose configuration
- Port mapping: 5000 (host) → 8080 (container)
- Health checks included

### Documentation

- `README.md` - Main documentation
- `DOCKER.md` - Docker instructions and quick start
- `API_TESTING.md` - API testing guide with curl examples
- Existing docs updated with implementation status

### Testing

- **272 passing** — xUnit unit + integration tests (`dotnet test Lovecraft.UnitTests`)
  - Existing suites: `AuthenticationTests`, `ServiceTests`, `RefreshTokenTests`, `ChatTests`, `MatchingTests`, `ForumTests` (all extended with rank/ACL coverage where applicable)
  - New suites (Roles & ACL spec):
    - `AppConfigServiceTests` — cache hits/misses, 1-hour TTL, fallback to `RankThresholds.Defaults` / `PermissionConfig.Defaults` on missing or invalid rows, `LogWarning` on parse failure
    - `EffectiveLevelTests` — unified 0–5 map: Novice=0, ActiveMember=1, FriendOfAloe=2, AloeCrew=3, Moderator=4, Admin=5; `For(user, computedRank) = Math.Max(rankLevel, staffLevel)`
    - `RankCalculatorTests` — top-down OR-matching from crew → novice, honours `RankOverride`, threshold boundary conditions
    - `AzureUserServiceTests` — counter increments retry 3× on ETag 412 and swallow exceptions so counter failures never fail the primary operation
    - `AclTests` — integration tests using `WebApplicationFactory<Program>` + a custom `TestAuthHandler` that injects `staffRole` claims; exercises `section.MinRank`, `topic.MinRank`, `topic.NoviceVisible`, `topic.NoviceCanReply` rejections (`INSUFFICIENT_RANK` / `MODERATOR_REQUIRED` / `ADMIN_REQUIRED`)
  - New suites (UserCache & performance):
    - `UserCacheTests` — `Set`/`Get`/`Remove`/`GetAll` correctness, snapshot independence, 300-element concurrent write safety, `LoadAsync` from a mocked `AsyncPageable<UserEntity>`
    - `AzureUserServiceCacheTests` — `GetUsersAsync` reads cache not Azure (verified via Moq), `GetUserByIdAsync` cache hit vs miss, all four write methods update the cache entry
    - `MockUserServiceShuffleTests` — shuffle preserves all items with no duplicates and produces different orderings across repeated calls
- Tests run with `dotnet test`
- Assembly-level `[CollectionBehavior(DisableTestParallelization = true)]` (in `Lovecraft.UnitTests/AssemblyInfo.cs`) serialises the entire suite — a pragmatic workaround for `MockDataStore` static state (tracked as `followup-mock-state-hygiene` for a proper migration off shared static state)

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

> **Note**: JWT authentication has since been implemented. See [AUTH_IMPLEMENTATION.md](./AUTH_IMPLEMENTATION.md) for details.

### Immediate Next Steps (Backend)
1. ~~Add JWT authentication~~ ✅ Done
2. ~~Integrate Azure Table Storage~~ ✅ Done — 8 Azure services, 17 entities, seeder tool
3. ~~Add chat endpoints~~ ✅ Done — REST + SignalR, 3 new tables, 18 new unit tests
4. Integrate Azure Blob Storage (image uploads)
5. Add email service (SMTP/SendGrid for verification and password reset)
6. Add songs endpoint (frontend currently falls back to mock data)
7. Add input validation (FluentValidation)
8. Add logging (Serilog)

### Frontend Integration
1. ~~Auth endpoints connected to backend~~ ✅ Done
2. ~~Implement token storage + protected routes~~ ✅ Done
3. ~~Wire all pages to backend API~~ ✅ Done
4. ~~Docker deployment~~ ✅ Done — nginx proxy on Azure VM
5. ~~Token refresh~~ ✅ Done — silent refresh in `apiClient` (401 deduplication), proactive refresh in `ProtectedRoute` (<5 min expiry)
6. ~~User-visible error handling~~ ✅ Done — sonner toasts via `showApiError` (`src/lib/apiError.ts`); success toasts on auth/save/reply
7. ~~Form validation~~ ✅ Done — react-hook-form + Zod on all auth, profile, and forum reply forms (`src/lib/validators.ts`)

### Advanced Features
1. OAuth integration (Google, Facebook, VK)
2. Telegram bot authentication
3. ~~SignalR for real-time messaging~~ ✅ Done (basic send/receive; typing indicators and online presence are future work)
4. Rate limiting and account lockout
5. Redis cache
6. Azure deployment

## Notes (Current State)

- JWT authentication fully operational; all content endpoints require `[Authorize]`
- **Azure Table Storage** active when `USE_AZURE_STORAGE=true` in `.env`; falls back to in-memory mock services when false
- **`UserCache`** — `Services/Caching/UserCache.cs`; a `ConcurrentDictionary<string, UserEntity>` singleton registered in DI. `Program.cs` calls `LoadAsync(TableClient)` on startup (Azure mode only) to populate it with every row from the `users` table before the first request is served. `AzureUserService.GetUsersAsync` and `GetUserByIdAsync` read from the cache; all five write methods (`UpdateUser`, `IncrementCounter`, `SetStaffRole`, `SetRankOverride`, and `AzureAuthService` user-creation paths) call `_cache.Set(entity)` after each successful Azure write. `GetUsersAsync` Fisher-Yates shuffles the cache snapshot before applying skip/take so the swipe deck order is random per request. `MockUserService.GetUsersAsync` also shuffles.
- **`appconfig` table** — partitions: `rank_thresholds` (10 integer rows), `permissions` (11 string rows), `registration` (1 row), and `pagination` (6 rows: `messages_initial`=30, `messages_batch`=20, `replies_initial`=20, `replies_batch`=15, `topics_initial`=25, `topics_batch`=15). Served by `AzureAppConfigService` with 1-hour `IMemoryCache`; falls back to typed defaults with `LogWarning` on missing/invalid rows. Seeded by `Lovecraft.Tools.Seeder`.
- **`IAppConfigService`** — singleton; 1-hour cached read of the `appconfig` table; fallback to code-defined defaults for missing rows. `IUserService` gained `IncrementCounterAsync` / `SetStaffRoleAsync` / `SetRankOverrideAsync`; counter increments in the Azure implementation retry 3× on ETag 412 and are wrapped in try/catch so counter failures never fail the primary operation. `IForumService` gained `UpdateTopicAsync`, cursor-based `GetRepliesAsync(topicId, cursor?)`, and offset-based `GetTopicsAsync(sectionId, page)`.
- **Infinite-scroll pagination** — `PagedResult<T>` now carries `NextCursor?` (string, Azure RowKey) and nullable `Total`. Chat messages and forum replies use `RowKey gt cursor` Azure Table filter (O(pageSize), no full-partition scan). Forum topics use in-memory sort (pinned-first, `UpdatedAt` desc) then skip/take. Page sizes are runtime-tunable via the `appconfig` `pagination` partition; `CachingForumService` keys the topics cache by `sectionId:page`.
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
- CORS allows localhost:8080, localhost:5173, and the Azure VM origin
- Access token: 15 min; Refresh token: 7 days (HttpOnly cookie)
- Enums serialize as camelCase strings in all API responses
- No rate limiting, no external logging, no email service yet
- **Deployed on Azure VM**: nginx proxies `/api/` on port 8080 to backend container; only port 8080 needs to be open

## Files Created

### Solution Files
- `Lovecraft.sln`
- `Dockerfile`
- `docker-compose.yml`

### Common Project (9 files)
- `Enums/Enums.cs`
- `DTOs/Auth/AuthDtos.cs`
- `DTOs/Blog/BlogDtos.cs`
- `DTOs/Chats/ChatDtos.cs`
- `DTOs/Events/EventDtos.cs`
- `DTOs/Forum/ForumDtos.cs`
- `DTOs/Matching/MatchingDtos.cs`
- `DTOs/Store/StoreDtos.cs`
- `DTOs/Users/UserDto.cs`
- `Models/ApiResponse.cs`

### Backend Project (10 files)
- `Program.cs`
- `MockData/MockDataStore.cs`
- `Services/IServices.cs`
- `Services/MockUserService.cs`
- `Services/MockEventService.cs`
- `Services/MockMatchingService.cs`
- `Services/MockStoreService.cs`
- `Services/MockBlogService.cs`
- `Services/MockForumService.cs`
- `Controllers/V1/UsersController.cs`
- `Controllers/V1/EventsController.cs`
- `Controllers/V1/MatchingController.cs`
- `Controllers/V1/StoreController.cs`
- `Controllers/V1/BlogController.cs`
- `Controllers/V1/ForumController.cs`

### Tests Project (1 file)
- `ServiceTests.cs`

### Documentation (3 files)
- `DOCKER.md`
- `API_TESTING.md`
- `IMPLEMENTATION_SUMMARY.md` (this file)

### Updated Files
- `README.md` - Added implementation status

**Total: 37 new files created + 1 updated**
