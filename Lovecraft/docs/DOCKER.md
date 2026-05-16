## LoveCraft Backend - Docker Instructions

> **Production deployment**: The full stack (frontend + backend) is managed by `docker-compose.yml` in `aloevera-harmony-meet/`. The backend is not exposed externally — nginx in the frontend container proxies `/api/` to the backend over the internal Docker network. See [aloevera-harmony-meet/docs/HTTPS_SETUP.md](../../../aloevera-harmony-meet/docs/HTTPS_SETUP.md) for the HTTPS/Cloudflare setup.
>
> The instructions below are for **local backend-only development**.

### Prerequisites

- Docker Desktop installed and running
- .NET 10 SDK (optional, only if you want to run without Docker)

### Quick Start with Docker Compose (local dev)

1. **Build and run the container**:
   ```bash
   docker-compose up --build
   ```

2. **Access the API**:
   - API: http://localhost:5000
   - Swagger UI: http://localhost:5000/swagger
   - Health Check: http://localhost:5000/health

3. **Stop the container**:
   ```bash
   docker-compose down
   ```

### Alternative: Build and Run with Docker CLI

1. **Build the Docker image**:
   ```bash
   docker build -t lovecraft-backend .
   ```

2. **Run the container**:
   ```bash
   docker run -d -p 5000:8080 --name lovecraft-api lovecraft-backend
   ```

3. **View logs**:
   ```bash
   docker logs lovecraft-api
   ```

4. **Stop and remove**:
   ```bash
   docker stop lovecraft-api
   docker rm lovecraft-api
   ```

### Local Development (without Docker)

1. **Restore dependencies**:
   ```bash
   dotnet restore
   ```

2. **Build the solution**:
   ```bash
   dotnet build
   ```

3. **Run the backend**:
   ```bash
   cd Lovecraft.Backend
   dotnet run
   ```

4. **Access the API**:
   - API: http://localhost:5000 (or check console output for port)
   - Swagger UI: http://localhost:5000/swagger

### Available API Endpoints

Once running, check Swagger UI at http://localhost:5000/swagger for full API documentation.

#### Quick endpoint list:

- **Health**: `GET /health`
- **Users**: 
  - `GET /api/v1/users` - Get list of users
  - `GET /api/v1/users/{id}` - Get user by ID
  - `PUT /api/v1/users/{id}` - Update user
- **Events**:
  - `GET /api/v1/events` - Get all events
  - `GET /api/v1/events/{id}` - Get event by ID
  - `POST /api/v1/events/{id}/register` - Register for event
  - `DELETE /api/v1/events/{id}/register` - Unregister from event
- **Matching**:
  - `POST /api/v1/matching/likes` - Send a like
  - `GET /api/v1/matching/likes/sent` - Get sent likes
  - `GET /api/v1/matching/likes/received` - Get received likes
  - `GET /api/v1/matching/matches` - Get matches
- **Store**:
  - `GET /api/v1/store` - Get store items
  - `GET /api/v1/store/{id}` - Get store item by ID
- **Blog**:
  - `GET /api/v1/blog` - Get blog posts
  - `GET /api/v1/blog/{id}` - Get blog post by ID
- **Forum**:
  - `GET /api/v1/forum/sections` - Get forum sections
  - `GET /api/v1/forum/sections/{sectionId}/topics` - Get topics

### Testing with curl

```bash
# Health check (public)
curl http://localhost:5000/health

# Login to get access token
TOKEN=$(curl -s -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"Test123!@#"}' \
  | jq -r '.data.accessToken')

# Get users (requires auth)
curl http://localhost:5000/api/v1/users \
  -H "Authorization: Bearer $TOKEN"

# Get events (requires auth)
curl http://localhost:5000/api/v1/events \
  -H "Authorization: Bearer $TOKEN"

# Get blog posts (requires auth)
curl http://localhost:5000/api/v1/blog \
  -H "Authorization: Bearer $TOKEN"
```

> **Note**: All `/api/v1/*` endpoints require a valid JWT Bearer token. Use Swagger UI for easier interactive testing — it has a built-in Authorize button.

### Current Status

Production-ready and deployed on Azure VM at `https://aloeve.club`. Mode is controlled by `USE_AZURE_STORAGE` (false → in-memory mock; true → Azure Table Storage).

**Implemented**:
- ✅ REST API: Auth, Users, Events, Matching, Store, Blog, Forum, Chats, Images, Admin
- ✅ JWT authentication: register/login/logout/refresh/verify-email/forgot/reset/change-password; access 15 min / refresh 7 d
- ✅ **Token refresh** — body (HTTP) or HttpOnly cookie (HTTPS); rotates token pair
- ✅ Password hashing (PBKDF2 + salt + 100k iterations)
- ✅ **Google Sign-In** (Google Identity Services ID token verification)
- ✅ **Telegram Login Widget** + **Telegram Mini App** authentication (HMAC verifiers, pending-ticket flow)
- ✅ **Telegram Bot worker** (`Lovecraft.TelegramBot`) — separate hosted service, long-poll
- ✅ Azure Table Storage (23 tables) with `Lovecraft.Tools.Seeder` CLI
- ✅ Azure Blob Storage (`profile-images` + `content-images`); 1200px resize + JPEG 85%
- ✅ Email delivery via SendGrid (falls back to `NullEmailService` logging when `SENDGRID_API_KEY` absent)
- ✅ Real-time messaging via SignalR (`/hubs/chat`)
- ✅ Rate limiting (sliding window 20 req/min/IP, shared bucket across auth endpoints; 429 + `Retry-After`)
- ✅ HTTPS in production via Cloudflare Origin Certificate on nginx
- ✅ Swagger/OpenAPI documentation
- ✅ CORS for `localhost:8080`, `localhost:5173`, `localhost:3000`, `aloeve.club`, `www.aloeve.club`
- ✅ Docker (multi-stage) + health checks
- ✅ ~25 test classes (~250+ tests)

**Not yet implemented**:
- ❌ Songs endpoint (frontend `songsApi.ts` always returns mock)
- ❌ Azure Blob SAS tokens (blobs currently public-read; profile images use `{userId}/{guid}.jpg` to avoid enumeration)
- ❌ Account lockout after failed logins
- ❌ Notifications, online presence, typing indicators
- ❌ Application Insights / structured logging

### Test Credentials

```
Email:    test@example.com
Password: Test123!@#
```

This user is pre-seeded with `EmailVerified = true` so you can log in immediately.

### Registration policy (appconfig)

There is **no** `INVITE_CODE` environment variable. Whether new accounts must supply an event invite code is determined by **Azure Table** appconfig:

- Partition: `registration`
- Row key: `require_event_invite` — when `true`, `Register` requires a valid `inviteCode`; when `false`, signup is open unless you choose to pass a code anyway. Codes may be **event** invites or **campaign** (non-event) invites with a negative id (e.g. `-1`).

The public endpoint `GET /api/v1/auth/registration-config` exposes this to the client as `requireEventInvite`. Invite codes are stored as **readable plaintext** in Table Storage (see [INVITES.md](./INVITES.md)). Admins create event invites with `POST /api/v1/admin/events/{eventId}/invites` and campaign invites with `POST /api/v1/admin/invites/campaigns`.

### Related docs

- [README.md](../../README.md) — main entry point
- [AUTHENTICATION.md](./AUTHENTICATION.md) — full auth surface (local, Google, Telegram)
- [AZURE_STORAGE.md](./AZURE_STORAGE.md) — table schema
- [TELEGRAM_AUTH.md](./TELEGRAM_AUTH.md) — Telegram Login Widget + Mini App + Bot worker
- [GOOGLE_OAUTH_SETUP.md](./GOOGLE_OAUTH_SETUP.md) — Google Cloud Console setup
- [INVITES.md](./INVITES.md) — event + campaign invite codes
