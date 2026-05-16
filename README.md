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
├── README.md                       # This file
└── Lovecraft/
    ├── Lovecraft.slnx              # Solution
    ├── Lovecraft.Common/           # Shared DTOs (Admin, Auth, Blog, Chats, Events,
    │                                  Forum, Images, Matching, Store, Users), enums, ApiResponse<T>
    ├── Lovecraft.Backend/          # ASP.NET Core API + SignalR
    │   ├── Auth/                   # JwtService, PasswordHasher,
    │   │                              TelegramLoginVerifier, TelegramWebAppVerifier,
    │   │                              GoogleIdTokenVerifier
    │   ├── Attributes/             # RequireStaffRoleAttribute, RequirePermissionAttribute
    │   ├── Configuration/          # JwtSettings, TelegramAuthOptions, GoogleAuthOptions
    │   ├── Controllers/V1/         # Admin, Auth, Blog, Chats, Events, Forum,
    │   │                              Images, Matching, Store, Users
    │   ├── Helpers/                # RankCalculator, EffectiveLevel, PermissionGuard,
    │   │                              EventForumAccess, EventTopicAccess, HtmlGuard
    │   ├── Hubs/                   # ChatHub (SignalR)
    │   ├── Services/               # IServices.cs + Mock implementations
    │   │   ├── Azure/              # 11 Azure-backed implementations
    │   │   ├── Caching/            # UserCache + IMemoryCache wrappers
    │   │   └── Email/              # SendGridEmailService, NullEmailService
    │   ├── Storage/                # TableNames + 22 entity classes
    │   ├── MockData/               # MockDataStore.cs
    │   └── Program.cs              # Startup, DI mode switch
    ├── Lovecraft.TelegramBot/      # Separate hosted-service worker (long-poll)
    ├── Lovecraft.Tools.Seeder/     # CLI: seed Azure Tables from mock data
    ├── Lovecraft.UnitTests/        # xUnit — ~25 test classes
    ├── Dockerfile                  # Backend image
    ├── Dockerfile.telegram-bot     # Bot worker image
    └── docs/                       # Technical documentation
```

---

## 📚 Documentation

In `Lovecraft/docs/`:

- **[QUICKSTART.md](./Lovecraft/docs/QUICKSTART.md)** — local dev quick start
- **[DOCKER.md](./Lovecraft/docs/DOCKER.md)** — Docker setup
- **[ARCHITECTURE.md](./Lovecraft/docs/ARCHITECTURE.md)** — system architecture
- **[IMPLEMENTATION_SUMMARY.md](./Lovecraft/docs/IMPLEMENTATION_SUMMARY.md)** — implementation log
- **[AUTHENTICATION.md](./Lovecraft/docs/AUTHENTICATION.md)** — full auth surface (local + Google + Telegram)
- **[TELEGRAM_AUTH.md](./Lovecraft/docs/TELEGRAM_AUTH.md)** — Telegram Login Widget + Mini App + Bot worker
- **[GOOGLE_OAUTH_SETUP.md](./Lovecraft/docs/GOOGLE_OAUTH_SETUP.md)** — Google Cloud Console setup
- **[AZURE_STORAGE.md](./Lovecraft/docs/AZURE_STORAGE.md)** — 23-table schema + blob containers
- **[CHAT_ARCHITECTURE.md](./Lovecraft/docs/CHAT_ARCHITECTURE.md)** — REST + SignalR chat design
- **[EVENTS.md](./Lovecraft/docs/EVENTS.md)** — event visibility, registration, forum-topic access
- **[INVITES.md](./Lovecraft/docs/INVITES.md)** — event + campaign invite codes
- **[API_TESTING.md](./Lovecraft/docs/API_TESTING.md)** — curl reference

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
# Storage mode
USE_AZURE_STORAGE=true|false              # false → in-memory mock
AZURE_STORAGE_CONNECTION_STRING=...
AZURE_TABLE_PREFIX=                        # optional: isolate dev/test datasets

# JWT
JWT_SECRET_KEY=<32+ random bytes>          # required

# Email (optional — falls back to NullEmailService console logging)
SENDGRID_API_KEY=<sendgrid api key>
FROM_EMAIL=noreply@aloeband.ru
FRONTEND_BASE_URL=https://aloeve.club

# Google sign-in (optional)
GOOGLE_OAUTH_CLIENT_ID=<web client id>.apps.googleusercontent.com

# Telegram (optional)
TELEGRAM_BOT_TOKEN=<from BotFather>
TELEGRAM_BOT_USERNAME=<bot username without @>
# Also bindable via Telegram__BotToken / Telegram__BotUsername / Google__ClientId
```

Registration gating is **not** an env var. It's the `appconfig` row `registration` / `require_event_invite`. `GET /api/v1/auth/registration-config` exposes it as `{ requireEventInvite: bool }` to the frontend. Invite codes are issued via the admin API and validated at registration. See [DOCKER.md](./Lovecraft/docs/DOCKER.md) and [INVITES.md](./Lovecraft/docs/INVITES.md).

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

### ✅ Shipped
- **REST API**: Admin, Auth, Blog, Chats, Events, Forum, Images, Matching, Store, Users
- **JWT auth**: register, login, logout, refresh, verify-email, forgot/reset/change-password (PBKDF2 + 16-byte salt + 100k iterations); access 15 min / refresh 7 d rotating
- **Google sign-in** (Google Identity Services ID token verification + pending-ticket flow)
- **Telegram Login Widget** + **Telegram Mini App** (HMAC verifiers, pending-ticket flow, attach-email for Telegram-only accounts)
- **Telegram Bot worker** (`Lovecraft.TelegramBot`) as separate hosted-service container
- **Account linking** across providers; smart email-based auto-linking for Google
- **Azure Table Storage** integration (23 tables, 11 Azure service implementations)
- **Azure Blob Storage** for `profile-images` + `content-images` (1200px resize, JPEG Q85)
- **External profile photo download** from Google/Telegram CDN to Azure Blob
- **`Lovecraft.Tools.Seeder`**: CLI to seed all 23 tables; respects `AZURE_TABLE_PREFIX`
- **SignalR `/hubs/chat`** for real-time chat + forum-reply notifications
- **Email delivery** via SendGrid; falls back to `NullEmailService` console logging
- **Rate limiting** (sliding window, 20 req/min/IP, shared bucket across auth endpoints; 429 + `Retry-After`)
- **HTTPS** in production via Cloudflare + Origin Certificate on nginx (deployed at https://aloeve.club)
- **HtmlGuard** input sanitization on user-facing fields (returns 400 `HTML_NOT_ALLOWED`)
- **Roles & ACL**: `appconfig`-backed rank thresholds + permissions; `[RequireStaffRole]` (sync) + `[RequirePermission]` (async); `staffRole` embedded as JWT claim
- **Event invites + campaign invites** in `eventinvites` table; admin API for create/rotate/revoke
- **Per-topic event forum visibility** (`public`/`attendeesOnly`/`specificUsers`)
- All C# enums serialize as camelCase strings
- Swagger UI at `/swagger`; health check at `/health`
- CORS for localhost dev + production origin
- xUnit tests (~25 test classes); integration tests via `WebApplicationFactory<Program>`

### 📋 Open
- Songs backend endpoint (frontend `songsApi.ts` still returns mock)
- Azure Blob SAS tokens (containers currently public-read)
- Account lockout after failed logins
- Online presence / typing indicators / notifications
- Application Insights / structured logging
- Admin moderation queue, user blocking, content removal
- Telegram Mini App polish (deep-link start params, command menu)

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
