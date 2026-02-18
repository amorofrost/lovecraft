# LoveCraft Backend

**AloeVera Harmony Meet** - .NET 10 Backend API

Lovecraft is the backend service for the AloeVera Harmony Meet platform, built with .NET 10, Azure Storage, and Docker.

> **📦 Current Status**: Mock implementation with stub API endpoints. All data is in-memory. See [DOCKER.md](./Lovecraft/DOCKER.md) for quick start instructions.

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
Lovecraft/
├── Lovecraft.sln             # Solution file
├── Lovecraft.Common/         # Shared DTOs, contracts, models
│   ├── DTOs/                 # Data Transfer Objects
│   ├── Enums/                # Enumerations
│   └── Models/               # Response models
├── Lovecraft.Backend/        # Main API project
│   ├── Controllers/          # REST API controllers
│   ├── Services/             # Business logic services
│   ├── MockData/             # Mock data store
│   └── Program.cs            # Application entry point
├── Lovecraft.UnitTests/      # Unit tests
├── Dockerfile                # Docker build configuration
├── docker-compose.yml        # Docker Compose configuration
├── DOCKER.md                 # Docker instructions
├── docs/                     # Technical documentation
└── README.md                 # This file
```

---

## 📚 Documentation

Comprehensive documentation is available in the `/docs` folder:

- **[ARCHITECTURE.md](./docs/ARCHITECTURE.md)** - System architecture and design
- **[AZURE_STORAGE.md](./docs/AZURE_STORAGE.md)** - Data schema and storage patterns
- **[API.md](./docs/API.md)** - Complete API specification _(to be created)_
- **[AUTHENTICATION.md](./docs/AUTHENTICATION.md)** - Auth design _(to be created)_
- **[DEVELOPMENT.md](./docs/DEVELOPMENT.md)** - Local setup guide _(to be created)_
- **[DEPLOYMENT.md](./docs/DEPLOYMENT.md)** - Azure deployment guide _(to be created)_

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

**📖 For detailed instructions, see [DOCKER.md](./Lovecraft/DOCKER.md)**

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

### ✅ Completed
- Project structure created
- Documentation written

### 🚧 In Progress
- Phase 1: Foundation setup

### 📋 Planned
- Phase 2: Authentication
- Phase 3: User Management
- Phase 4: Matching System
- Phase 5: Events
- Phase 6: Messaging
- Phase 7: Community Features
- Phase 8: Store Integration
- Phase 9: Frontend Integration
- Phase 10: Deployment
- Phase 11: Real-time Messaging
- Phase 12: Optimization

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
