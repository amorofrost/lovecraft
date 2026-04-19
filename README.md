# LoveCraft Backend

**AloeVera Harmony Meet** - .NET 10 Backend API

Lovecraft is the backend service for the AloeVera Harmony Meet platform, built with .NET 10, Azure Storage, and Docker.

> **📦 Current Status**: Full-stack deployed on Azure VM. JWT auth, Azure Table Storage integration, and Docker Compose with nginx proxy are all operational. All REST API endpoints running, data persists across restarts. See [DOCKER.md](./Lovecraft/docs/DOCKER.md) for quick start instructions.

---

## 🎯 Project Overview

**LoveCraft** is a RESTful API that powers multiple client applications:
- **Web Application** (React/TypeScript) - Primary client
- **Telegram Mini App** (JavaScript) - Planned
- **Mobile Apps** (iOS/Android) - Future

### Technology Stack

- **.NET 10** - ASP.NET Core Web API
- **Azure Table Storage** - NoSQL data storage
- **Azure Blob Storage** - Image storage
- **Docker** - Containerization
- **JWT** - Authentication
- **xUnit** - Unit testing

---

## 📁 Repository Structure

```
lovecraft/
├── README.md                 # This file
└── Lovecraft/
    ├── Lovecraft.sln         # Solution file
    ├── Lovecraft.Common/     # Shared DTOs, enums, models
    │   ├── DTOs/             # Auth, Users, Events, Matching, Store, Blog, Forum, Chat DTOs
    │   ├── Enums/            # All enumerations
    │   └── Models/           # ApiResponse<T>
    ├── Lovecraft.Backend/    # Main API project
    │   ├── Auth/             # JwtService, PasswordHasher, JwtSettings
    │   ├── Controllers/V1/   # AuthController + all resource controllers
    │   ├── Services/         # IServices.cs + Mock*Service implementations
    │   ├── MockData/         # MockDataStore.cs — in-memory seed data
    │   └── Program.cs        # Application startup
    ├── Lovecraft.UnitTests/  # xUnit tests (22 tests)
    ├── Dockerfile            # Multi-stage Docker build
    ├── docker-compose.yml    # Docker Compose
    └── docs/                 # Technical documentation
```

---

## 📚 Documentation

Comprehensive documentation is available in the `Lovecraft/docs/` folder:

- **[QUICKSTART.md](./Lovecraft/docs/QUICKSTART.md)** - 30-second start guide
- **[DOCKER.md](./Lovecraft/docs/DOCKER.md)** - Docker setup and commands
- **[IMPLEMENTATION_SUMMARY.md](./Lovecraft/docs/IMPLEMENTATION_SUMMARY.md)** - What's implemented
- **[AUTHENTICATION.md](./Lovecraft/docs/AUTHENTICATION.md)** - Auth design and flows
- **[AUTH_IMPLEMENTATION.md](./Lovecraft/docs/AUTH_IMPLEMENTATION.md)** - Auth implementation details
- **[AUTH_FLOWS.md](./Lovecraft/docs/AUTH_FLOWS.md)** - Authentication flow diagrams
- **[AUTH_DECISIONS.md](./Lovecraft/docs/AUTH_DECISIONS.md)** - Auth design decisions
- **[API_TESTING.md](./Lovecraft/docs/API_TESTING.md)** - API testing with curl/Swagger
- **[ARCHITECTURE.md](./Lovecraft/docs/ARCHITECTURE.md)** - System architecture
- **[AZURE_STORAGE.md](./Lovecraft/docs/AZURE_STORAGE.md)** - Data schema and storage patterns
- **[EVENTS.md](./Lovecraft/docs/EVENTS.md)** - Events: visibility, registration, interest, forum topics & per-topic access

---

## 🚀 Quick Start

### Prerequisites

- **.NET 10 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Docker Desktop** - [Download](https://www.docker.com/products/docker-desktop)

### Option 1: Docker Compose (Recommended)

```bash
cd Lovecraft
docker-compose up --build
```

### Option 2: .NET CLI

```bash
cd Lovecraft
dotnet build
cd Lovecraft.Backend
dotnet run
```

### Access the API

- **API**: http://localhost:5000
- **Swagger UI**: http://localhost:5000/swagger
- **Health Check**: http://localhost:5000/health

**📖 For detailed instructions, see [DOCKER.md](./Lovecraft/docs/DOCKER.md)**

---

## ⚙️ Configuration

### Environment Variables

```bash
# Azure Storage (or use mock data)
AZURE_STORAGE_CONNECTION_STRING=DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...

# JWT Authentication
JWT_SECRET=your-super-secret-key-change-in-production
JWT_ISSUER=https://api.aloevera-meet.com
JWT_AUDIENCE=https://aloevera-meet.com

# CORS (allowed origins)
ALLOWED_ORIGINS=http://localhost:8080,https://aloevera-meet.com

# Mock Data (for development)
USE_MOCK_DATA=true
```

Registration is **not** controlled by a global `INVITE_CODE` environment variable. The frontend reads `GET /api/v1/auth/registration-config` (`requireEventInvite`), which mirrors the **appconfig** row `registration` / `require_event_invite`. Per-event invite codes are issued via the admin API and validated at registration. See [DOCKER.md](./Lovecraft/docs/DOCKER.md#registration-policy-appconfig).

### appsettings.json

```json
{
  "AzureStorage": {
    "ConnectionString": "UseDevelopmentStorage=true",
    "TablePrefix": "lovecraft"
  },
  "Jwt": {
    "Secret": "your-secret-key-here",
    "Issuer": "https://localhost:5000",
    "Audience": "https://localhost:8080",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:8080"]
  }
}
```

---

## 🧪 Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true
```

---

## 🐳 Docker

### Local Development

```bash
# Using docker-compose
docker-compose up
```

### Production Build

```bash
# Build
docker build -t lovecraft-backend:latest -f Lovecraft.Backend/Dockerfile .

# Run
docker run -p 80:80 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e AZURE_STORAGE_CONNECTION_STRING="..." \
  lovecraft-backend:latest
```

---

## 📝 Development Workflow

### 1. Create a Feature Branch

```bash
git checkout -b feature/add-user-search
```

### 2. Make Changes

- Add/modify code in appropriate project
- Add unit tests
- Update documentation if needed

### 3. Run Tests

```bash
dotnet test
```

### 4. Commit Changes

```bash
git add .
git commit -m "feat: add user search endpoint"
```

### 5. Push and Create PR

```bash
git push origin feature/add-user-search
# Create Pull Request on GitHub
```

---

## 🏗️ Implementation Status

### ✅ Completed
- Project structure: `Lovecraft.Common`, `Lovecraft.Backend`, `Lovecraft.UnitTests`, `Lovecraft.Tools.Seeder`
- All REST API controllers: Auth, Users, Events, Matching, Store, Blog, Forum
- **JWT Authentication**: register, login, logout, token refresh, email verification, password reset, change password
- Password hashing (PBKDF2 + salt)
- **Azure Table Storage integration**: 7 Azure service implementations, 14 entity classes, 15 table constants; mode switch via `USE_AZURE_STORAGE` env var
- **`Lovecraft.Tools.Seeder`**: CLI tool that seeds all 15 Azure tables from mock data (users with hashed passwords, events, store, blog, forum)
- `[Authorize]` enforced on all content controllers (Events, Store, Blog, Forum, Users, Matching)
- **Enum serialization**: all C# enums serialize as camelCase strings (e.g., `"concert"`, `"male"`, `"nonBinary"`)
- Forum topics & replies: 12 topics and 25 replies; full topic detail and reply CRUD endpoints
- CORS configured for frontend (localhost:8080, localhost:5173)
- Swagger UI at `/swagger`; health check at `/health`
- **Docker + nginx proxy**: frontend container proxies `/api/` to backend; only port 8080 needs to be exposed; deployed on Azure VM
- **Token refresh endpoint** (`POST /api/v1/auth/refresh`): accepts refresh token in request body (localStorage flow) or HttpOnly cookie (HTTPS flow); issues new rotated access + refresh token pair; `Secure` cookie flag is conditional on `Request.IsHttps` so it works over HTTP too
- **35 unit tests** (16 auth + 6 service + 13 refresh-token tests) — all passing
- Frontend API service layer fully implemented for all domains; all pages wired to backend
- **Silent token refresh in frontend**: `apiClient` retries any 401 response after refreshing; concurrent 401s are deduplicated; `ProtectedRoute` proactively refreshes tokens near expiry (<5 min)

### 📋 Planned
- Azure Blob Storage (image uploads — images currently Unsplash URLs)
- Email service (SMTP/SendGrid — tokens currently logged to console)
- OAuth integration (Google, Facebook, VK)
- Telegram bot authentication
- Real-time messaging (SignalR)
- Chat and songs endpoints (frontend currently uses mock data for these)
- Rate limiting and account lockout

See [BACKEND_PLAN.md](../aloevera-harmony-meet/docs/BACKEND_PLAN.md) for detailed roadmap.

---

## 🤝 Contributing

### Code Style

- Follow .NET conventions
- Use async/await for I/O
- Write unit tests for business logic
- Document public APIs with XML comments

### Naming Conventions

- **Classes**: `PascalCase`
- **Methods**: `PascalCase` + `Async` suffix
- **Properties**: `PascalCase`
- **Parameters**: `camelCase`
- **Private fields**: `_camelCase`

### Before Committing

- [ ] Code builds without errors
- [ ] All tests pass
- [ ] No linter warnings
- [ ] Documentation updated
- [ ] API changes documented

---

## 📊 Project Statistics

- **Language**: C# (.NET 10)
- **Lines of Code**: TBD
- **Test Coverage**: Target 70%+
- **API Endpoints**: TBD (planned 50+)

---

## 🔗 Related Repositories

- **Web Application**: `@aloevera-harmony-meet/` - React web client (separate repo)
- **Telegram Bot**: `@aloevera-telegram-bot/` - Telegram Mini App (future, separate repo)
- **Mobile Apps**: `@aloevera-mobile/` - iOS/Android apps (future, separate repo)
- **Backend API**: `@lovecraft/` - This repository

**Architecture Philosophy**: Each client application is in its own repository. This backend serves all clients via a unified REST API.

---

## 📞 Support

For questions or issues:
- Check documentation in `/docs`
- Review API specification
- Contact team

---

## 📄 License

- **[MIT LICENSE](./LICENSE)** 

---

**Built with ❤️ for AloeVera fans**
