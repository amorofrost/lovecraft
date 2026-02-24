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
# Health check (public)
curl http://localhost:5000/health

# Login and capture token
TOKEN=$(curl -s -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"Test123!@#"}' \
  | jq -r '.data.accessToken')

# Get users (auth required)
curl http://localhost:5000/api/v1/users \
  -H "Authorization: Bearer $TOKEN"

# Get events (auth required)
curl http://localhost:5000/api/v1/events \
  -H "Authorization: Bearer $TOKEN"

# Get blog posts (auth required)
curl http://localhost:5000/api/v1/blog \
  -H "Authorization: Bearer $TOKEN"
```

## What's Working

✅ All REST API endpoints (Users, Events, Matching, Store, Blog, Forum)  
✅ **JWT Authentication** — register, login, logout, refresh, email verify, password reset  
✅ **Token refresh** — `POST /api/v1/auth/refresh` accepts token in request body or HttpOnly cookie; issues rotated token pair  
✅ Password hashing (PBKDF2 + salt, 100k iterations)  
✅ Mock data (prefixed with "Backend Mock:" in titles)  
✅ Swagger documentation with Authorize button  
✅ Docker support with health checks  
✅ 35 unit tests, all passing  
✅ CORS configured for frontend  
✅ Frontend API service layer connected (auth endpoints wired in Welcome.tsx)  

## What's NOT Working Yet

❌ Azure Storage — all data is in-memory (resets on restart)  
❌ Email delivery — verification tokens are logged to console only  
❌ Real-time messaging (SignalR)  
❌ OAuth (Google, Facebook, VK)  
❌ Telegram authentication  
❌ Frontend AuthContext — token returned from login is not yet stored  
❌ Frontend protected routes  

## Test Credentials

```
Email:    test@example.com
Password: Test123!@#
```

## Next Steps

1. **Frontend**: Implement `AuthContext` to store and refresh JWT tokens
2. **Frontend**: Add protected route wrapper
3. **Frontend**: Wire remaining pages (Friends, AloeVera, Talks) to backend API
4. **Backend**: Integrate Azure Table Storage (replace mock services)
5. **Backend**: Add email service (SMTP/SendGrid) for verification emails
6. **Backend**: OAuth integration

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
