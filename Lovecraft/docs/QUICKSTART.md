# 🚀 Quick Start

Full-stack project deployed at https://aloeve.club. This guide covers **local development** only — for production deployment via Docker + Cloudflare, see [aloevera-harmony-meet/docs/HTTPS_SETUP.md](../../../aloevera-harmony-meet/docs/HTTPS_SETUP.md).

## Start in 30 seconds (backend only, mock storage)

### Option 1: Docker

```bash
cd Lovecraft
docker compose up --build
```

Open http://localhost:5000/swagger

### Option 2: .NET CLI

```bash
cd Lovecraft
dotnet run --project Lovecraft.Backend
```

Open http://localhost:5000/swagger (or check console for port)

> The backend defaults to `USE_AZURE_STORAGE=false` (in-memory mock). To run against real Azure tables, set `USE_AZURE_STORAGE=true` and `AZURE_STORAGE_CONNECTION_STRING=...` in `.env`. See [DOCKER.md](./DOCKER.md) for full configuration.

## Test credentials

```
Email:    test@example.com
Password: Test123!@#
```

Pre-seeded with `EmailVerified=true`. Mock storage also seeds `user1@mock.local`–`user4@mock.local` with password `Seed123!@#`.

## Test the API

### Swagger UI

1. Open http://localhost:5000/swagger
2. `POST /api/v1/auth/login` with the test credentials
3. Copy the `accessToken` from the response
4. Click **Authorize** at the top, paste as `Bearer <token>`
5. All other endpoints are now accessible

### curl

```bash
# Health (public)
curl http://localhost:5000/health

# Login → capture token
TOKEN=$(curl -s -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"Test123!@#"}' \
  | jq -r '.data.accessToken')

# Authenticated endpoints
curl http://localhost:5000/api/v1/users   -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/events  -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/blog    -H "Authorization: Bearer $TOKEN"
```

## What's working

- ✅ REST API: Auth, Users, Events, Matching, Store, Blog, Forum, Chats, Images, Admin
- ✅ **JWT authentication**: email/password + Google Identity Services + Telegram Login Widget + Telegram Mini App
- ✅ **Token refresh** — body or HttpOnly cookie; rotates token pair
- ✅ Password hashing (PBKDF2 + salt + 100k iterations)
- ✅ Azure Table Storage (23 tables) with `Lovecraft.Tools.Seeder`
- ✅ Azure Blob Storage for profile + content images
- ✅ **Real-time messaging** via SignalR (`/hubs/chat`)
- ✅ **Email delivery** via SendGrid (falls back to console logging)
- ✅ **Rate limiting** (20 req/min/IP across login/register/forgot-password/reset/google/telegram endpoints)
- ✅ **Telegram Bot worker** (`Lovecraft.TelegramBot`)
- ✅ Swagger with Authorize button
- ✅ Docker + health checks
- ✅ Frontend wired end-to-end against this backend (see frontend repo)

## Not yet implemented

- ❌ Songs endpoint (frontend mock only)
- ❌ Azure Blob SAS tokens (containers currently public-read)
- ❌ Account lockout after failed logins
- ❌ Notifications / typing indicators / online presence
- ❌ Application Insights / structured logging

## Running tests

```bash
cd Lovecraft
dotnet test
```

All tests pass. Assembly-level `[CollectionBehavior(DisableTestParallelization = true)]` serializes the suite because `MockDataStore` is static.

## Project layout

```
Lovecraft/
├── Lovecraft.slnx                  # Solution
├── Lovecraft.Common/               # Shared DTOs + enums
├── Lovecraft.Backend/              # ASP.NET Core API + SignalR
├── Lovecraft.TelegramBot/          # Hosted-service Telegram long-poll worker
├── Lovecraft.Tools.Seeder/         # CLI: seed Azure Tables from mock data
├── Lovecraft.UnitTests/            # xUnit
├── Dockerfile                      # Backend image
├── Dockerfile.telegram-bot         # Bot worker image
└── docs/                           # This folder
```

## More docs

- [DOCKER.md](./DOCKER.md) — Docker / local dev
- [AUTHENTICATION.md](./AUTHENTICATION.md) — full auth surface
- [TELEGRAM_AUTH.md](./TELEGRAM_AUTH.md) — Telegram-specific flows
- [GOOGLE_OAUTH_SETUP.md](./GOOGLE_OAUTH_SETUP.md) — Google Cloud Console setup
- [INVITES.md](./INVITES.md) — invite codes
- [EVENTS.md](./EVENTS.md) — event visibility, registration, forum-topic access
- [AZURE_STORAGE.md](./AZURE_STORAGE.md) — table schema
- [CHAT_ARCHITECTURE.md](./CHAT_ARCHITECTURE.md) — SignalR + chat design
- [ARCHITECTURE.md](./ARCHITECTURE.md) — system architecture
- [IMPLEMENTATION_SUMMARY.md](./IMPLEMENTATION_SUMMARY.md) — implementation log
- [API_TESTING.md](./API_TESTING.md) — curl reference

## Troubleshooting

**Port already in use:**
```bash
docker compose down
```

**Can't access API:**
```bash
docker ps                      # is the container running?
docker compose logs            # what does it say?
curl http://localhost:5000/health
```

**Telegram widget shows disabled** — BotFather `/setdomain` must include the origin you're testing from.

**Google sign-in fails with `GOOGLE_TOKEN_INVALID`** — `GOOGLE_OAUTH_CLIENT_ID` on the backend must match the client ID the frontend uses. See [GOOGLE_OAUTH_SETUP.md](./GOOGLE_OAUTH_SETUP.md).
