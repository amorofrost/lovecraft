# LoveCraft Backend Architecture

**AloeVera Harmony Meet** - Technical Backend Architecture

**Version**: 1.0  
**Last Updated**: February 17, 2026  
**Technology**: .NET 10, Azure Storage, Docker

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
├── Lovecraft.sln                           # Solution file
│
├── Lovecraft.Common/                       # Shared library
│   ├── DTOs/                               # Data Transfer Objects
│   │   ├── Auth/
│   │   │   ├── LoginRequestDto.cs
│   │   │   ├── RegisterRequestDto.cs
│   │   │   ├── AuthResponseDto.cs
│   │   │   └── TokenResponseDto.cs
│   │   ├── Users/
│   │   │   ├── UserDto.cs
│   │   │   ├── UserProfileDto.cs
│   │   │   ├── UserPreferencesDto.cs
│   │   │   └── UserSettingsDto.cs
│   │   ├── Events/
│   │   │   ├── EventDto.cs
│   │   │   ├── EventDetailsDto.cs
│   │   │   └── EventRegistrationDto.cs
│   │   ├── Matching/
│   │   │   ├── LikeDto.cs
│   │   │   ├── MatchDto.cs
│   │   │   └── MatchDetailsDto.cs
│   │   ├── Chats/
│   │   │   ├── ChatDto.cs
│   │   │   ├── MessageDto.cs
│   │   │   └── ChatDetailsDto.cs
│   │   ├── Forum/
│   │   │   ├── ForumSectionDto.cs
│   │   │   ├── ForumTopicDto.cs
│   │   │   └── ForumReplyDto.cs
│   │   ├── Store/
│   │   │   └── StoreItemDto.cs
│   │   └── Blog/
│   │       └── BlogPostDto.cs
│   │
│   ├── Contracts/                          # Interfaces
│   │   ├── Services/
│   │   │   ├── IUserService.cs
│   │   │   ├── IAuthService.cs
│   │   │   ├── IMatchingService.cs
│   │   │   └── ...
│   │   └── Repositories/
│   │       ├── IUserRepository.cs
│   │       ├── IEventRepository.cs
│   │       └── ...
│   │
│   ├── Models/                             # Common models
│   │   ├── ApiResponse.cs
│   │   ├── ErrorResponse.cs
│   │   ├── PagedResult.cs
│   │   └── ValidationError.cs
│   │
│   ├── Enums/                              # Enumerations
│   │   ├── Gender.cs
│   │   ├── EventCategory.cs
│   │   ├── ChatType.cs
│   │   └── ...
│   │
│   ├── Constants/                          # Constants
│   │   ├── ErrorCodes.cs
│   │   ├── ValidationMessages.cs
│   │   └── StorageConstants.cs
│   │
│   └── Lovecraft.Common.csproj
│
├── Lovecraft.Backend/                      # Main API project
│   ├── Controllers/                        # API Controllers
│   │   ├── V1/
│   │   │   ├── AuthController.cs
│   │   │   ├── UsersController.cs
│   │   │   ├── EventsController.cs
│   │   │   ├── MatchesController.cs
│   │   │   ├── LikesController.cs
│   │   │   ├── ChatsController.cs
│   │   │   ├── ForumController.cs
│   │   │   ├── StoreController.cs
│   │   │   └── BlogController.cs
│   │   └── HealthController.cs
│   │
│   ├── Services/                           # Business logic
│   │   ├── UserService.cs
│   │   ├── AuthService.cs
│   │   ├── MatchingService.cs
│   │   ├── EventService.cs
│   │   ├── ChatService.cs
│   │   ├── ForumService.cs
│   │   ├── StoreService.cs
│   │   ├── BlogService.cs
│   │   ├── ImageService.cs
│   │   └── TokenService.cs
│   │
│   ├── Repositories/                       # Data access
│   │   ├── UserRepository.cs
│   │   ├── EventRepository.cs
│   │   ├── MatchRepository.cs
│   │   ├── LikeRepository.cs
│   │   ├── ChatRepository.cs
│   │   ├── MessageRepository.cs
│   │   ├── ForumRepository.cs
│   │   ├── StoreRepository.cs
│   │   ├── BlogRepository.cs
│   │   └── BaseRepository.cs
│   │
│   ├── Entities/                           # Database entities
│   │   ├── UserEntity.cs
│   │   ├── EventEntity.cs
│   │   ├── MatchEntity.cs
│   │   ├── LikeEntity.cs
│   │   ├── ChatEntity.cs
│   │   ├── MessageEntity.cs
│   │   └── ...
│   │
│   ├── Middleware/                         # Middleware components
│   │   ├── AuthenticationMiddleware.cs
│   │   ├── ErrorHandlingMiddleware.cs
│   │   ├── RequestLoggingMiddleware.cs
│   │   └── RateLimitingMiddleware.cs
│   │
│   ├── Configuration/                      # Configuration classes
│   │   ├── AzureStorageConfig.cs
│   │   ├── JwtConfig.cs
│   │   ├── CorsConfig.cs
│   │   └── AppSettings.cs
│   │
│   ├── Extensions/                         # Extension methods
│   │   ├── ServiceCollectionExtensions.cs
│   │   ├── TableEntityExtensions.cs
│   │   └── ClaimsPrincipalExtensions.cs
│   │
│   ├── Validators/                         # Input validators
│   │   ├── LoginRequestValidator.cs
│   │   ├── RegisterRequestValidator.cs
│   │   ├── UserProfileValidator.cs
│   │   └── ...
│   │
│   ├── Mappings/                           # AutoMapper profiles
│   │   ├── UserMappingProfile.cs
│   │   ├── EventMappingProfile.cs
│   │   └── ...
│   │
│   ├── MockData/                           # Mock data (initial phase)
│   │   ├── MockUsers.cs
│   │   ├── MockEvents.cs
│   │   └── ...
│   │
│   ├── Program.cs                          # Entry point
│   ├── appsettings.json                    # Configuration
│   ├── appsettings.Development.json        # Dev configuration
│   ├── appsettings.Production.json         # Prod configuration
│   ├── Dockerfile                          # Docker image
│   ├── .dockerignore                       # Docker ignore
│   └── Lovecraft.Backend.csproj
│
├── Lovecraft.UnitTests/                    # Unit tests
│   ├── Services/
│   │   ├── UserServiceTests.cs
│   │   ├── AuthServiceTests.cs
│   │   ├── MatchingServiceTests.cs
│   │   └── ...
│   ├── Controllers/
│   │   ├── AuthControllerTests.cs
│   │   ├── UsersControllerTests.cs
│   │   └── ...
│   ├── Repositories/
│   │   ├── UserRepositoryTests.cs
│   │   └── ...
│   ├── Helpers/
│   │   ├── MockDataHelper.cs
│   │   └── TestFixture.cs
│   └── Lovecraft.UnitTests.csproj
│
├── docs/                                   # Documentation
│   ├── API.md                              # API specification
│   ├── ARCHITECTURE.md                     # This file
│   ├── AZURE_STORAGE.md                    # Storage design
│   ├── AUTHENTICATION.md                   # Auth design
│   ├── DEPLOYMENT.md                       # Deployment guide
│   ├── DEVELOPMENT.md                      # Dev setup
│   └── TESTING.md                          # Testing guide
│
├── scripts/                                # Utility scripts
│   ├── setup-azure.ps1                     # Azure setup
│   ├── deploy.ps1                          # Deployment
│   ├── run-tests.ps1                       # Run tests
│   └── generate-jwt-secret.ps1             # JWT secret
│
├── .gitignore
├── .editorconfig
├── docker-compose.yml                      # Local development
├── docker-compose.prod.yml                 # Production
└── README.md
```

---

## 🔧 Technology Choices

### Core Framework
- **.NET 10** (latest LTS)
- **ASP.NET Core** for REST API
- **C# 13** (latest language features)

### Data Access
- **Azure.Data.Tables** - Azure Table Storage SDK
- **Azure.Storage.Blobs** - Azure Blob Storage SDK
- No ORM needed (NoSQL)

### Authentication
- **System.IdentityModel.Tokens.Jwt** - JWT handling
- **BCrypt.Net** or **Argon2** - Password hashing

### Dependency Injection
- Built-in ASP.NET Core DI container

### API Documentation
- **Swashbuckle** (Swagger/OpenAPI)

### Validation
- **FluentValidation** - Input validation

### Mapping
- **AutoMapper** - DTO/Entity mapping

### Testing
- **xUnit** - Unit testing framework
- **Moq** - Mocking framework
- **FluentAssertions** - Assertion library

### Logging
- **Serilog** - Structured logging
- **Application Insights** (production)

### Configuration
- **appsettings.json** - Configuration
- **Environment variables** - Secrets
- **Azure Key Vault** (production)

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
  "role": "user",
  "iat": 1234567890,
  "exp": 1234568790
}
```

**Refresh Token** (7 days expiry):
```json
{
  "sub": "user-guid",
  "token_type": "refresh",
  "jti": "refresh-token-id",
  "iat": 1234567890,
  "exp": 1235172690
}
```

### Security Measures

1. **HTTPS Only**: All communication encrypted
2. **Password Hashing**: BCrypt with salt (cost factor 12)
3. **JWT Signing**: HMAC-SHA256 with secret key
4. **Token Expiration**: Short-lived access tokens
5. **CORS**: Restricted to known origins
6. **Rate Limiting**: Prevent abuse
7. **Input Validation**: All inputs validated
8. **SQL Injection**: N/A (NoSQL Table Storage)
9. **XSS Prevention**: API returns JSON only
10. **Secrets Management**: Environment vars / Key Vault

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
