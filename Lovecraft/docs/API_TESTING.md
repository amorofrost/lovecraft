# API Testing Guide

All `/api/v1/*` endpoints (except `/auth/register`, `/auth/login`, `/auth/refresh`, and the public config endpoints) require a JWT Bearer token. Use Swagger's Authorize button for interactive testing, or capture the token via curl as shown below.

> Quick test for the dual modes: with `USE_AZURE_STORAGE=false` (default), data is in-memory and resets on restart. With `USE_AZURE_STORAGE=true`, data persists in Azure Tables.

## Health check (public)

```bash
curl http://localhost:5000/health
```

```json
{ "status": "Healthy", "timestamp": "...", "version": "1.0.0", "authentication": "Enabled" }
```

## Capture an access token

```bash
TOKEN=$(curl -s -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"Test123!@#"}' \
  | jq -r '.data.accessToken')

echo "$TOKEN"
```

Pre-seeded test user: `test@example.com` / `Test123!@#` (already `EmailVerified=true`). Mock storage also seeds `user1@mock.local`–`user4@mock.local` with password `Seed123!@#`.

## Auth surface (rate-limited at 20 req/min/IP except where noted)

```bash
# Register
curl -X POST http://localhost:5000/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"new@example.com","password":"MyPass123!@#","name":"New User","age":25,"location":"City","gender":"male","bio":"hi"}'

# Refresh (NOT rate-limited; body OR HttpOnly cookie)
curl -X POST http://localhost:5000/api/v1/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<token from login response>"}'

# Current user
curl http://localhost:5000/api/v1/auth/me \
  -H "Authorization: Bearer $TOKEN"

# Registration policy (public)
curl http://localhost:5000/api/v1/auth/registration-config

# Google sign-in (public)
curl http://localhost:5000/api/v1/auth/google-config
curl -X POST http://localhost:5000/api/v1/auth/google-login \
  -H "Content-Type: application/json" \
  -d '{"idToken":"<google id token jwt>"}'

# Telegram Login Widget (public)
curl http://localhost:5000/api/v1/auth/telegram-login-config
# Body is the raw widget payload (id, first_name, auth_date, hash, ...)
curl -X POST http://localhost:5000/api/v1/auth/telegram-login \
  -H "Content-Type: application/json" \
  -d '{"id":12345,"first_name":"Test","auth_date":1700000000,"hash":"..."}'

# Telegram Mini App (public)
curl -X POST http://localhost:5000/api/v1/auth/telegram-miniapp-login \
  -H "Content-Type: application/json" \
  -d '{"initData":"<raw initData query string>"}'

# Attach email to a Telegram-only or Google-only account
curl -X POST http://localhost:5000/api/v1/auth/attach-email \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"email":"add@example.com","password":"MyPass123!@#"}'

# Forgot / reset password
curl -X POST http://localhost:5000/api/v1/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com"}'

curl -X POST http://localhost:5000/api/v1/auth/reset-password \
  -H "Content-Type: application/json" \
  -d '{"token":"<reset token from email/console>","newPassword":"NewPass123!@#"}'
```

See [AUTHENTICATION.md](./AUTHENTICATION.md) for the full auth surface and pending-ticket flow.

## Users

```bash
curl http://localhost:5000/api/v1/users -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/users/1 -H "Authorization: Bearer $TOKEN"
curl -X PUT http://localhost:5000/api/v1/users/1 \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"Anna Updated","age":26,"bio":"Updated bio","location":"Moscow","gender":"female"}'

# Profile photo upload (multipart)
curl -X POST http://localhost:5000/api/v1/users/1/images \
  -H "Authorization: Bearer $TOKEN" \
  -F "image=@/path/to/photo.jpg"
```

## Events

```bash
curl http://localhost:5000/api/v1/events -H "Authorization: Bearer $TOKEN"
# Detail; optional ?code=<invite> unlocks secret events
curl 'http://localhost:5000/api/v1/events/1?code=MOCK-ATTEND-1' \
  -H "Authorization: Bearer $TOKEN"

# Register (non-staff must include inviteCode)
curl -X POST http://localhost:5000/api/v1/events/1/register \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"inviteCode":"MOCK-ATTEND-1"}'

# Interest (separate from attendance — no invite needed)
curl -X POST   http://localhost:5000/api/v1/events/1/interest -H "Authorization: Bearer $TOKEN"
curl -X DELETE http://localhost:5000/api/v1/events/1/interest -H "Authorization: Bearer $TOKEN"
```

Mock storage auto-seeds `MOCK-ATTEND-{eventId}` for every event on startup so tests can register without admin steps.

## Matching

```bash
# Send a like
curl -X POST http://localhost:5000/api/v1/matching/likes \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"toUserId":"2"}'

curl http://localhost:5000/api/v1/matching/likes/sent     -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/matching/likes/received -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/matching/matches        -H "Authorization: Bearer $TOKEN"
```

Mutual like auto-creates a 1-on-1 chat via `IChatService.GetOrCreateChatAsync`.

## Store, Blog

```bash
curl http://localhost:5000/api/v1/store    -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/store/s1 -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/blog     -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/blog/b1  -H "Authorization: Bearer $TOKEN"
```

## Forum

```bash
curl http://localhost:5000/api/v1/forum/sections -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/forum/sections/general/topics -H "Authorization: Bearer $TOKEN"

# Create a topic
curl -X POST http://localhost:5000/api/v1/forum/sections/general/topics \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"title":"Hello world","content":"first topic","noviceVisible":true,"noviceCanReply":true}'

curl http://localhost:5000/api/v1/forum/topics/<id>         -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/forum/topics/<id>/replies -H "Authorization: Bearer $TOKEN"
curl -X POST http://localhost:5000/api/v1/forum/topics/<id>/replies \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content":"hello"}'

# Event discussions (server-filtered by per-topic visibility)
curl http://localhost:5000/api/v1/forum/event-discussions/summary    -H "Authorization: Bearer $TOKEN"
curl http://localhost:5000/api/v1/forum/event-discussions/1/topics   -H "Authorization: Bearer $TOKEN"
```

## Chats

```bash
curl http://localhost:5000/api/v1/chats -H "Authorization: Bearer $TOKEN"

# Get-or-create private chat (idempotent)
curl -X POST http://localhost:5000/api/v1/chats \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"targetUserId":"2"}'

# Send + broadcast over SignalR
curl -X POST http://localhost:5000/api/v1/chats/<chatId>/messages \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content":"hi","imageUrls":[]}'

# Paginated history
curl 'http://localhost:5000/api/v1/chats/<chatId>/messages?page=1&pageSize=50' \
  -H "Authorization: Bearer $TOKEN"
```

## Images

```bash
curl -X POST http://localhost:5000/api/v1/images/upload \
  -H "Authorization: Bearer $TOKEN" \
  -F "image=@/path/to/photo.jpg"
# → { "Url": "https://<storage>.blob.core.windows.net/content-images/..." }
```

JPEG/PNG/GIF/WebP, max 10 MB. Backend resizes to 1200 px max and re-encodes as JPEG Q85.

## Admin (admin-only)

```bash
curl http://localhost:5000/api/v1/admin/config -H "Authorization: Bearer $ADMIN_TOKEN"

# Set staff role
curl -X PUT http://localhost:5000/api/v1/users/<userId>/role \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"staffRole":"moderator"}'

# Create an event invite code
curl -X POST http://localhost:5000/api/v1/admin/events/<eventId>/invites \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"expiresAtUtc":"2027-01-01T00:00:00Z"}'

# Campaign code (non-event)
curl -X POST http://localhost:5000/api/v1/admin/invites/campaigns \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"campaignId":-1,"campaignLabel":"launch","expiresAtUtc":"2027-01-01T00:00:00Z"}'
```

## SignalR (`/hubs/chat`)

JWT goes in the query string because WebSocket can't send headers after the handshake:

```
wss://localhost:5001/hubs/chat?access_token=<jwt>
```

Hub methods: `JoinChat(chatId)`, `JoinTopic(topicId)`, `LeaveGroup(groupId)`, `SendMessage(chatId, content)`. Server events: `MessageReceived(MessageDto)`, `ReplyPosted(ForumReplyDto, topicId)`.

See [CHAT_ARCHITECTURE.md](./CHAT_ARCHITECTURE.md) for the full design.

## Swagger UI

The easiest way to test is interactively via Swagger:

1. Start the backend
2. Open http://localhost:5000/swagger
3. Click **Authorize** at the top and paste `Bearer <accessToken>`
4. All endpoints are now accessible from the UI

## Response format

All responses follow `ApiResponse<T>`:

```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "timestamp": "..."
}
```

```json
{
  "success": false,
  "data": null,
  "error": { "code": "...", "message": "...", "details": {} },
  "timestamp": "..."
}
```

Standard error codes: `INVALID_CREDENTIALS`, `INVALID_REFRESH_TOKEN`, `WEAK_PASSWORD`, `EMAIL_TAKEN`, `INVALID_INVITE_CODE`, `INVITE_REQUIRED`, `INSUFFICIENT_RANK`, `MODERATOR_REQUIRED`, `ADMIN_REQUIRED`, `HTML_NOT_ALLOWED`, `TOO_MANY_REQUESTS`, `GOOGLE_TOKEN_INVALID`, `TELEGRAM_AUTH_FAILED`.

## Mock data

When `USE_AZURE_STORAGE=false` the API returns `MockDataStore` content (4 users, 10 events, 4 store items, 3 blog posts, 4 forum sections + 12 topics + 25 replies, 3 songs). Mock storage also auto-seeds `MOCK-ATTEND-{eventId}` invite codes at startup.
