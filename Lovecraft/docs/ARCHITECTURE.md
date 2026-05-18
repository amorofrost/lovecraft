# LoveCraft Backend Architecture

**AloeVera Harmony Meet — backend architecture**

**Last Updated**: 2026-05-15
**Technology**: .NET 10 / ASP.NET Core, Azure Table Storage + Blob Storage, SignalR, Docker

**Domain docs:** [AUTHENTICATION.md](./AUTHENTICATION.md), [TELEGRAM_AUTH.md](./TELEGRAM_AUTH.md), [EVENTS.md](./EVENTS.md), [INVITES.md](./INVITES.md), [CHAT_ARCHITECTURE.md](./CHAT_ARCHITECTURE.md), [AZURE_STORAGE.md](./AZURE_STORAGE.md).

---

## 📐 System Overview

LoveCraft backend is a RESTful API service built with .NET 10 (ASP.NET Core) that serves multiple client applications. It uses Azure Storage (Tables + Blobs) for data persistence and is designed to scale horizontally.

### Design Principles

1. **Stateless API**: No session state on servers
2. **Cloud-Native**: Optimized for Azure
3. **Multi-Client**: Supports web, Telegram, mobile
4. **Scalable**: Horizontal scaling via containers
5. **Secure**: JWT authentication, HTTPS only
6. **Testable**: Dependency injection, unit tests
7. **Observable**: Logging and monitoring built-in

---

## 🏗️ Architecture Layers

```
┌────────────────────────────────────────────────────────┐
│                 Client Applications                     │
│  (Web, Telegram Mini App, Mobile - future)             │
└────────────────────┬───────────────────────────────────┘
                     │ HTTPS / REST / JSON
                     ▼
┌────────────────────────────────────────────────────────┐
│                  API Layer (.NET)                       │
│  ┌──────────────────────────────────────────────────┐  │
│  │              Controllers                          │  │
│  │  - AuthController                                 │  │
│  │  - UsersController                                │  │
│  │  - EventsController                               │  │
│  │  - MatchesController                              │  │
│  │  - ChatsController                                │  │
│  │  - ForumController                                │  │
│  │  - StoreController                                │  │
│  │  - BlogController                                 │  │
│  └──────────────────┬───────────────────────────────┘  │
│                     │                                   │
│  ┌──────────────────▼───────────────────────────────┐  │
│  │             Middleware                            │  │
│  │  - Authentication (JWT validation)                │  │
│  │  - Error Handling                                 │  │
│  │  - Request Logging                                │  │
│  │  - CORS                                           │  │
│  │  - Rate Limiting                                  │  │
│  └──────────────────┬───────────────────────────────┘  │
└────────────────────┬┴───────────────────────────────────┘
                     │
┌────────────────────▼───────────────────────────────────┐
│              Business Logic Layer                       │
│  ┌──────────────────────────────────────────────────┐  │
│  │                 Services                          │  │
│  │  - UserService (profiles, search, settings)      │  │
│  │  - AuthService (registration, login, JWT)        │  │
│  │  - MatchingService (likes, matches, algorithm)   │  │
│  │  - EventService (events, registrations)          │  │
│  │  - ChatService (chats, messages)                 │  │
│  │  - ForumService (sections, topics, replies)      │  │
│  │  - StoreService (catalog)                        │  │
│  │  - BlogService (posts)                           │  │
│  │  - ImageService (uploads, storage)               │  │
│  └──────────────────┬───────────────────────────────┘  │
└────────────────────┬┴───────────────────────────────────┘
                     │
┌────────────────────▼───────────────────────────────────┐
│              Data Access Layer                          │
│  ┌──────────────────────────────────────────────────┐  │
│  │              Repositories                         │  │
│  │  - UserRepository                                 │  │
│  │  - EventRepository                                │  │
│  │  - MatchRepository                                │  │
│  │  - ChatRepository                                 │  │
│  │  - ForumRepository                                │  │
│  │  - StoreRepository                                │  │
│  │  - BlogRepository                                 │  │
│  └──────────────────┬───────────────────────────────┘  │
│                     │                                   │
│  ┌──────────────────▼───────────────────────────────┐  │
│  │          Azure Storage Client                     │  │
│  │  - TableClient (Azure SDK)                        │  │
│  │  - BlobClient (Azure SDK)                         │  │
│  └──────────────────┬───────────────────────────────┘  │
└────────────────────┬┴───────────────────────────────────┘
                     │
┌────────────────────▼───────────────────────────────────┐
│                  Azure Storage                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐    │
│  │   Table     │  │    Blob     │  │   Queue     │    │
│  │  Storage    │  │  Storage    │  │  (Future)   │    │
│  └─────────────┘  └─────────────┘  └─────────────┘    │
└─────────────────────────────────────────────────────────┘
```

---

## 📦 Project Structure

```
Lovecraft/
├── Lovecraft.slnx                          # Solution
│
├── Lovecraft.Common/                       # Shared DTOs + enums
│   ├── DTOs/                               # Admin, Auth, Blog, Chats, Events,
│   │                                          Forum, Images, Matching, Store, Users
│   ├── Enums/                              # Gender, EventCategory, ChatType,
│   │                                          MessageType, ProfileVisibility,
│   │                                          ShowMePreference, Language,
│   │                                          EventTopicVisibility, UserRank, StaffRole
│   ├── Models/                             # ApiResponse<T>, PagedResult<T>
│   └── Lovecraft.Common.csproj
│
├── Lovecraft.Backend/                      # Main ASP.NET Core Web API + SignalR
│   ├── Controllers/V1/                     # Admin, Auth, Blog, Chats, Events,
│   │                                          Forum, Images, Matching, Store, Users
│   ├── Auth/                               # JwtService, JwtSettings, PasswordHasher,
│   │                                          TelegramLoginVerifier (Login Widget HMAC),
│   │                                          TelegramWebAppVerifier (Mini App HMAC),
│   │                                          GoogleIdTokenVerifier (Google JWKS verify)
│   ├── Attributes/                         # RequireStaffRoleAttribute (sync, claim-only),
│   │                                          RequirePermissionAttribute (async, reads appconfig)
│   ├── Configuration/                      # JwtSettings, TelegramAuthOptions, GoogleAuthOptions
│   ├── Helpers/                            # RankCalculator, EffectiveLevel, PermissionGuard,
│   │                                          EventForumAccess, EventTopicAccess, HtmlGuard,
│   │                                          AppRuntime
│   ├── Hubs/ChatHub.cs                     # SignalR hub (JoinChat, JoinTopic, SendMessage)
│   ├── Services/                           # IServices.cs + Mock implementations
│   │   ├── Azure/                          # 14 Azure-backed services (Auth, User, Event,
│   │   │                                      Matching, Store, Blog, Forum, Chat, Image,
│   │   │                                      AppConfig, EventInvite, Notification,
│   │   │                                      NotificationPreference, PushSubscription)
│   │   ├── Caching/                        # UserCache (ConcurrentDictionary singleton,
│   │   │                                      LoadAsync on startup) + IMemoryCache wrappers
│   │   │                                      (CachingEventService, CachingStoreService,
│   │   │                                      CachingBlogService, CachingForumService)
│   │   ├── Email/                          # IEmailService, SendGridEmailService,
│   │   │                                      NullEmailService (chosen by SENDGRID_API_KEY presence)
│   │   ├── Notifications/                  # INotificationService, INotificationProducer,
│   │   │                                      NotificationPolicy, NotificationDeduper,
│   │   │                                      IPresenceTracker, IInAppDispatcher
│   │   ├── AppConfig.cs                    # RankThresholds.Defaults, PermissionConfig.Defaults
│   │   ├── EventInviteHelpers.cs           # Code generation
│   │   ├── EventInviteNormalizer.cs        # Trim + uppercase normalisation
│   │   ├── InvalidInviteCodeException.cs   # Maps to INVALID_INVITE_CODE
│   │   └── InviteRequiredException.cs      # Maps to INVITE_REQUIRED
│   ├── Storage/
│   │   ├── TableNames.cs                   # 27 table names + AZURE_TABLE_PREFIX support
│   │   └── Entities/                       # 22 entity classes (see AZURE_STORAGE.md)
│   ├── MockData/MockDataStore.cs           # Static in-memory seed when USE_AZURE_STORAGE=false
│   ├── Program.cs                          # DI mode switch, JWT, SignalR, rate limiting, CORS
│   ├── appsettings.json
│   └── Lovecraft.Backend.csproj
│
├── Lovecraft.TelegramBot/                  # Separate hosted-service worker
│   ├── Program.cs                          # Host.CreateApplicationBuilder + AddHostedService
│   ├── TelegramBotWorker.cs                # Long-poll worker
│   └── Lovecraft.TelegramBot.csproj
│
├── Lovecraft.Tools.Seeder/                 # CLI: seed Azure Tables from MockDataStore
│
├── Lovecraft.UnitTests/                    # xUnit
│   ├── AuthenticationTests, RefreshTokenTests, ServiceTests
│   ├── TelegramLoginVerifierTests, TelegramPendingFlowTests, TelegramMiniAppFlowTests
│   ├── GooglePendingFlowTests
│   ├── AppConfigServiceTests, EffectiveLevelTests, RankCalculatorTests, AclTests
│   ├── UserCacheTests, AzureUserServiceTests, CachingTests
│   ├── ChatTests, MatchingTests, ForumTests, EventInviteServiceTests, EventTopicAccessTests
│   ├── ImageTests, EmailServiceTests, HtmlGuardTests, RateLimitingTests
│   ├── UsersControllerUpdateTests, TestAuthDependencies
│   └── AssemblyInfo.cs                     # [CollectionBehavior(DisableTestParallelization=true)]
│
├── docs/                                   # Documentation (this folder)
│   ├── ARCHITECTURE.md (this file), AUTHENTICATION.md, TELEGRAM_AUTH.md,
│   │   GOOGLE_OAUTH_SETUP.md, AZURE_STORAGE.md, CHAT_ARCHITECTURE.md,
│   │   EVENTS.md, INVITES.md, DOCKER.md, QUICKSTART.md, API_TESTING.md,
│   │   IMPLEMENTATION_SUMMARY.md
│
├── Dockerfile                              # Backend image
├── Dockerfile.telegram-bot                 # Bot worker image
└── README.md
```

> The frontend repository's `docker-compose.yml` orchestrates three services: `frontend` (nginx + SPA), `backend` (this project), and `telegram-bot` (worker).

---

## 🔧 Technology Choices

### Core
- **.NET 10** / ASP.NET Core
- C# 13

### Data
- `Azure.Data.Tables` (Table Storage)
- `Azure.Storage.Blobs` (image storage)
- No ORM (NoSQL)
- In-process caches: `UserCache` (ConcurrentDictionary singleton, `LoadAsync` on startup) + `IMemoryCache` wrappers for Event/Store/Blog/Forum/AppConfig (1-hour TTL)

### Authentication
- `Microsoft.AspNetCore.Authentication.JwtBearer` — Bearer JWT
- Custom `PasswordHasher` (PBKDF2-HMAC-SHA256, 100k iterations, random 16-byte salt)
- `Google.Apis.Auth` — Google ID token verification (JWKS)
- Custom HMAC verifiers for Telegram Login Widget + Mini App `initData`

### Real-time
- `Microsoft.AspNetCore.SignalR` — `/hubs/chat`, JWT via query string

### Email
- `SendGrid` SDK when `SENDGRID_API_KEY` is set, otherwise `NullEmailService` (console logging)

### Image processing
- `ImageMagick` / `SixLabors.ImageSharp` (resize + JPEG re-encode)

### Rate limiting
- Built-in `Microsoft.AspNetCore.RateLimiting`, sliding window, shared bucket per IP

### Dependency Injection
- Built-in ASP.NET Core container

### API documentation
- Swashbuckle (Swagger UI at `/swagger`)

### Testing
- xUnit + Moq + `WebApplicationFactory<Program>` for integration tests
- `Microsoft.AspNetCore.Mvc.Testing` for `AclTests` end-to-end auth filter coverage

### Configuration
- `appsettings.json` + environment variables / `.env`
- `AZURE_TABLE_PREFIX` for isolated dataset namespaces

### Not used (despite older mentions)
- ❌ FluentValidation — using DataAnnotations + manual validation
- ❌ AutoMapper — using hand-rolled mappers (e.g. `ToDto` extension methods)
- ❌ Repositories layer — services talk directly to `TableClient` / `BlobClient`
- ❌ Serilog / Application Insights — not yet integrated

---

## 🔐 Security Architecture

### Authentication Flow

1. **Registration**:
   ```
   Client → POST /api/v1/auth/register
   Backend → Hash password (BCrypt)
   Backend → Store user in Azure Table Storage
   Backend → Generate JWT tokens
   Backend → Return tokens to client
   ```

2. **Login**:
   ```
   Client → POST /api/v1/auth/login
   Backend → Validate credentials
   Backend → Generate JWT access + refresh tokens
   Backend → Return tokens to client
   ```

3. **Authenticated Request**:
   ```
   Client → GET /api/v1/users/me (with Bearer token)
   Backend → Validate JWT signature
   Backend → Extract claims (userId, email, role)
   Backend → Process request
   Backend → Return response
   ```

4. **Token Refresh**:
   ```
   Client → POST /api/v1/auth/refresh (with refresh token)
   Backend → Validate refresh token
   Backend → Generate new access token
   Backend → Return new access token
   ```

### JWT Structure

**Access Token** (15 min expiry):
```json
{
  "sub": "user-guid",
  "email": "user@example.com",
  "name": "User Name",
  "staffRole": "none | moderator | admin",
  "iat": 1234567890,
  "exp": 1234568790,
  "iss": "AloeVeraAPI",
  "aud": "AloeVeraClients"
}
```

`staffRole` is embedded so `[RequireStaffRole]` can authorise without hitting storage. The user's computed rank (Novice / ActiveMember / FriendOfAloe / AloeCrew) is **not** in the JWT — `[RequirePermission]` reads it from storage via `IUserService` on each request.

**Refresh Token** — opaque random string stored hashed in the `refreshtokens` table with `ExpiresAt`, `RevokedAt`, `ReplacedByTokenId` (rotation chain).

### Security Measures (current)

1. **HTTPS** via Cloudflare → Origin Certificate on nginx (port 443 only; port 80 redirects)
2. **Password hashing**: PBKDF2-HMAC-SHA256, 100k iterations, random 16-byte salt per password
3. **JWT signing**: HMAC-SHA256; access 15 min, refresh 7 d, rotating refresh tokens
4. **CORS**: restricted to `localhost:{8080,5173,3000}` and `aloeve.club`/`www.aloeve.club`
5. **Rate limiting**: sliding window, 20 req/min/IP, shared bucket across auth endpoints
6. **Input sanitization**: `HtmlGuard` rejects HTML tags in forum/chat/user-update inputs (returns 400 `HTML_NOT_ALLOWED`)
7. **SQL injection**: N/A (NoSQL Table Storage)
8. **XSS**: React auto-escapes; BB-code renderer uses no `dangerouslySetInnerHTML`
9. **Secrets**: env vars / `.env` (Azure Key Vault planned)
10. **Telegram payload integrity**: HMAC verification with replay window (24 h widget / 1 h Mini App)
11. **Google ID token verification**: full signature check against JWKS, audience + issuer + expiry checks

---

## 📊 Data Flow Examples

### Example 1: User Registration

```
┌─────────┐                ┌─────────┐                ┌─────────┐
│ Client  │                │ Backend │                │ Azure   │
│  (Web)  │                │   API   │                │ Storage │
└────┬────┘                └────┬────┘                └────┬────┘
     │                          │                          │
     │ POST /auth/register      │                          │
     │ {email, password, ...}   │                          │
     ├─────────────────────────>│                          │
     │                          │                          │
     │                          │ Validate input           │
     │                          │ Hash password            │
     │                          │                          │
     │                          │ Insert user entity       │
     │                          ├─────────────────────────>│
     │                          │                          │
     │                          │<─────────────────────────┤
     │                          │ Success                  │
     │                          │                          │
     │                          │ Generate JWT tokens      │
     │                          │                          │
     │ {accessToken, refreshToken}                         │
     │<─────────────────────────┤                          │
     │                          │                          │
```

### Example 2: Like a User (Match Detection)

```
┌─────────┐                ┌─────────┐                ┌─────────┐
│ Client  │                │ Backend │                │ Azure   │
└────┬────┘                └────┬────┘                └────┬────┘
     │                          │                          │
     │ POST /likes              │                          │
     │ {toUserId: "abc"}        │                          │
     │ Authorization: Bearer... │                          │
     ├─────────────────────────>│                          │
     │                          │                          │
     │                          │ Validate JWT             │
     │                          │ Extract fromUserId       │
     │                          │                          │
     │                          │ Check if already liked   │
     │                          ├─────────────────────────>│
     │                          │<─────────────────────────┤
     │                          │                          │
     │                          │ Insert like entity       │
     │                          ├─────────────────────────>│
     │                          │<─────────────────────────┤
     │                          │                          │
     │                          │ Check reverse like       │
     │                          ├─────────────────────────>│
     │                          │<─────────────────────────┤
     │                          │                          │
     │                          │ IF reverse like exists:  │
     │                          │   Create match entity    │
     │                          ├─────────────────────────>│
     │                          │<─────────────────────────┤
     │                          │                          │
     │ {isMatch: true, match: {...}}                       │
     │<─────────────────────────┤                          │
     │                          │                          │
```

---

## 🔄 Request/Response Flow

### Middleware Pipeline

```
HTTP Request
    │
    ▼
┌─────────────────────────┐
│ HTTPS Redirection       │
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ CORS Policy             │
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ Request Logging         │
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ Authentication          │ ← JWT validation
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ Authorization           │ ← Role/Policy checks
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ Rate Limiting           │
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ Controller              │ ← Your code
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ Service Layer           │
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ Repository Layer        │
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ Azure Storage           │
└───────────┬─────────────┘
            │
            ▼ (Response)
┌─────────────────────────┐
│ Error Handling          │ ← Catch exceptions
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────┐
│ Response Logging        │
└───────────┬─────────────┘
            │
            ▼
    HTTP Response
```

---

## 🚀 Scalability Design

### Horizontal Scaling

The API is **stateless** and can scale horizontally:

```
           Load Balancer
                │
    ┌───────────┼───────────┐
    │           │           │
    ▼           ▼           ▼
Backend 1   Backend 2   Backend 3
    │           │           │
    └───────────┴───────────┘
                │
                ▼
         Azure Storage
```

**Key Points**:
- No session state on servers
- All state in Azure Storage or JWT
- Any instance can handle any request
- Auto-scale based on CPU/memory

### Future: Caching Layer

```
Backend → Redis Cache → Azure Storage
              ↓
          (Cache Hit)
              ↓
           Return
```

**Cache Strategy**:
- User profiles (frequently accessed)
- Event lists
- Store catalog
- Forum topics
- TTL: 5-15 minutes

### Future: Azure Orleans

```
Backend API
    │
    ▼
Orleans Cluster
    │
    ├─ UserGrain (user-1)
    ├─ UserGrain (user-2)
    ├─ EventGrain (event-1)
    └─ ChatGrain (chat-1)
        │
        ▼
  Azure Storage
```

**Benefits**:
- Actor model (user = grain)
- In-memory state
- Virtual actors (created on demand)
- Automatic persistence
- Distributed transactions

---

## 📈 Performance Considerations

### Query Optimization

**Azure Table Storage Best Practices**:
1. **PartitionKey**: Design for query patterns
2. **RowKey**: Use meaningful identifiers
3. **Point Queries**: Fastest (PartitionKey + RowKey)
4. **Range Queries**: Efficient (PartitionKey + RowKey range)
5. **Table Scans**: Avoid (slow and expensive)

**Example Partition Strategies**:
- Users: PartitionKey = `USER#{firstLetter}`, RowKey = `userId`
- Events: PartitionKey = `EVENT#{category}`, RowKey = `eventId`
- Likes: PartitionKey = `LIKE#{fromUserId}`, RowKey = `toUserId`
- Messages: PartitionKey = `CHAT#{chatId}`, RowKey = `{timestamp}#{messageId}`

### Image Optimization

1. **Upload**: Direct to Blob Storage (SAS token)
2. **Resize**: Azure Function (on upload event)
3. **CDN**: Azure CDN in front of Blob Storage
4. **Format**: WebP for modern browsers
5. **Compression**: Optimize before upload

### API Response Times

**Target Response Times**:
- Authentication: < 200ms
- User profile: < 100ms
- User search: < 300ms
- Event list: < 200ms
- Message send: < 150ms

**Strategies**:
- Async/await throughout
- Connection pooling
- Efficient queries
- Caching (future)
- CDN for static content

---

## 🧪 Testing Strategy

### Unit Tests (70%)

Test business logic in isolation:
- Services (mocked repositories)
- Validators
- Mappers
- Utilities

**Example**:
```csharp
[Fact]
public async Task RegisterUser_ValidInput_ReturnsSuccess()
{
    // Arrange
    var mockRepo = new Mock<IUserRepository>();
    var service = new AuthService(mockRepo.Object);
    var request = new RegisterRequestDto { ... };

    // Act
    var result = await service.RegisterAsync(request);

    // Assert
    result.Should().NotBeNull();
    result.Success.Should().BeTrue();
}
```

### Integration Tests (20%)

Test with real or in-memory Azure Storage:
- Controllers (end-to-end)
- Repositories
- Authentication flow

### Manual Tests (10%)

- Postman collections
- Swagger UI testing
- Frontend integration

---

## 📝 Code Standards

### Naming Conventions

- **Classes**: PascalCase (`UserService`)
- **Methods**: PascalCase (`GetUserAsync`)
- **Properties**: PascalCase (`UserId`)
- **Parameters**: camelCase (`userId`)
- **Private fields**: _camelCase (`_userRepository`)
- **Constants**: UPPER_SNAKE_CASE (`MAX_PAGE_SIZE`)

### Async/Await

- All I/O operations are async
- Methods suffixed with `Async`
- Use `ConfigureAwait(false)` in libraries

### Error Handling

```csharp
try
{
    // Operation
}
catch (ValidationException ex)
{
    return BadRequest(new ErrorResponse
    {
        Code = "VALIDATION_ERROR",
        Message = ex.Message
    });
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error");
    return StatusCode(500, new ErrorResponse
    {
        Code = "INTERNAL_ERROR",
        Message = "An unexpected error occurred"
    });
}
```

### Dependency Injection

```csharp
public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserService> _logger;
    private readonly IMapper _mapper;

    public UserService(
        IUserRepository userRepository,
        ILogger<UserService> logger,
        IMapper mapper)
    {
        _userRepository = userRepository;
        _logger = logger;
        _mapper = mapper;
    }
}
```

---

## 🔍 Monitoring & Observability

### Logging

**Structured Logging with Serilog**:
```csharp
_logger.LogInformation(
    "User {UserId} liked user {TargetUserId}",
    fromUserId,
    toUserId
);
```

**Log Levels**:
- `Trace`: Very detailed (development only)
- `Debug`: Debugging information
- `Information`: General flow (default)
- `Warning`: Unexpected but handled
- `Error`: Errors and exceptions
- `Critical`: Critical failures

### Metrics

**Application Insights** (production):
- Request/response times
- Error rates
- Dependency calls
- Custom events

**Health Checks**:
```
GET /health
{
  "status": "Healthy",
  "checks": {
    "azureStorage": "Healthy",
    "database": "Healthy"
  }
}
```

---

## 📚 References

- [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core)
- [Azure Table Storage Best Practices](https://docs.microsoft.com/azure/storage/tables/)
- [Azure Blob Storage Documentation](https://docs.microsoft.com/azure/storage/blobs/)
- [JWT Best Practices](https://tools.ietf.org/html/rfc8725)
- [RESTful API Design](https://restfulapi.net/)

---

**Next**: See [API.md](./API.md) for complete API specification
