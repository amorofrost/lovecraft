# LoveCraft Backend

**AloeVera Harmony Meet** - .NET 10 Backend API

Lovecraft is the backend service for the AloeVera Harmony Meet platform, built with .NET 10, Azure Storage, and Docker.

> **📦 Current Status**: Working mock implementation with JWT authentication. All REST API endpoints are running and connected to the frontend. JWT auth is fully implemented, enums serialize as camelCase strings, all content endpoints require authentication (`[Authorize]`), and the full stack runs end-to-end in Docker. All data is in-memory (no Azure Storage yet). See [DOCKER.md](./Lovecraft/docs/DOCKER.md) for quick start instructions.

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

### ✅ Completed (Mock Implementation)
- Project structure: `Lovecraft.Common`, `Lovecraft.Backend`, `Lovecraft.UnitTests`
- All REST API controllers: Auth, Users, Events, Matching, Store, Blog, Forum
- **JWT Authentication**: register, login, logout, token refresh, email verification, password reset, change password
- Password hashing (PBKDF2 + salt)
- All mock services with in-memory data
- `[Authorize]` enforced on all content controllers (Events, Store, Blog, Forum, Users, Matching)
- **Enum serialization**: all C# enums serialize as camelCase strings (e.g., `"concert"`, `"male"`, `"nonBinary"`) for frontend compatibility
- **Forum topics**: `MockDataStore` now contains 12 detailed forum topics across 4 sections; `MockForumService` filters by `sectionId`
- CORS configured for frontend (localhost:8080, localhost:5173)
- Swagger UI at `/swagger`
- Health check at `/health`
- Docker + docker-compose support (full-stack tested end-to-end)
- **22 unit tests** (16 auth + 6 service tests) — all passing
- Frontend API service layer fully implemented for all domains
- All frontend pages wired to backend (events, store, blog, forum, matching, users)
- Frontend token stored in `localStorage`; protected routes guard all content pages

### 📋 Planned (Backend)
- Azure Table Storage integration (replace in-memory mock services)
- Azure Blob Storage (image uploads)
- Email service (SMTP/SendGrid for verification and password reset)
- OAuth integration (Google, Facebook, VK)
- Telegram bot authentication
- Real-time messaging (SignalR)
- Rate limiting and account lockout
- Azure deployment

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

[Your license here]

---

**Built with ❤️ for AloeVera fans**
