# 🚀 Quick Start Guide

This is a **working stub implementation** of the LoveCraft backend with mock data.

## Start in 30 Seconds

### Option 1: Docker (Recommended)

```bash
docker-compose up --build
```

Open http://localhost:5000/swagger

### Option 2: .NET CLI

```bash
dotnet run --project Lovecraft.Backend
```

Open http://localhost:5000/swagger (or check console for port)

## Test the API

### Using Swagger UI (Easiest)

1. Open http://localhost:5000/swagger
2. Click on any endpoint
3. Click "Try it out"
4. Click "Execute"

### Using curl

```bash
# Health check
curl http://localhost:5000/health

# Get users
curl http://localhost:5000/api/v1/users

# Get events
curl http://localhost:5000/api/v1/events

# Get blog posts
curl http://localhost:5000/api/v1/blog
```

## What's Working

✅ All REST API endpoints  
✅ Mock data (matches frontend)  
✅ Swagger documentation  
✅ Docker support  
✅ Unit tests (6 tests, all passing)  
✅ CORS enabled for frontend  

## What's NOT Working Yet

❌ Authentication (JWT)  
❌ Azure Storage  
❌ Data persistence  
❌ Input validation  
❌ Real-time messaging  

## Next Steps

1. **Frontend Integration**: Update the React app to call these APIs
2. **Authentication**: Implement JWT auth
3. **Azure Storage**: Replace mock services with real storage
4. **Validation**: Add FluentValidation
5. **Logging**: Add Serilog

## Documentation

- [README.md](../README.md) - Main documentation
- [DOCKER.md](./DOCKER.md) - Docker instructions
- [API_TESTING.md](./API_TESTING.md) - API testing guide
- [IMPLEMENTATION_SUMMARY.md](./IMPLEMENTATION_SUMMARY.md) - What was implemented
- [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) - Architecture overview
- [docs/AZURE_STORAGE.md](./docs/AZURE_STORAGE.md) - Storage schema
- [docs/BACKEND_PLAN.md](../aloevera-harmony-meet/docs/BACKEND_PLAN.md) - Implementation plan

## Project Structure

```
Lovecraft/
├── Lovecraft.sln              # Solution
├── Lovecraft.Common/          # Shared DTOs
├── Lovecraft.Backend/         # API implementation
├── Lovecraft.UnitTests/       # Tests
├── Dockerfile                 # Docker build
└── docker-compose.yml         # Docker compose
```

## Run Tests

```bash
dotnet test
```

All 6 tests should pass.

## API Endpoints

See [API_TESTING.md](./API_TESTING.md) for complete list or check Swagger UI at http://localhost:5000/swagger

**Base URL**: `http://localhost:5000`

- `/health` - Health check
- `/api/v1/users` - Users
- `/api/v1/events` - Events
- `/api/v1/matching` - Likes & matches
- `/api/v1/store` - Store items
- `/api/v1/blog` - Blog posts
- `/api/v1/forum` - Forum sections

## Troubleshooting

**Port already in use:**
```bash
# Change port in docker-compose.yml or use:
docker-compose down
```

**Docker not building:**
```bash
# Clean and rebuild:
docker-compose down
docker-compose up --build --force-recreate
```

**Can't access API:**
- Check if container is running: `docker ps`
- Check logs: `docker-compose logs`
- Try: http://localhost:5000/health

## Questions?

Check the documentation files above or look at the source code - it's well-commented!
