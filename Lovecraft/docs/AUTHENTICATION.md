# Authentication & Authorization

**AloeVera Harmony Meet — LoveCraft Backend**

**Last Updated**: 2026-05-15
**Status**: Email/password, Google Identity Services, Telegram Login Widget, and Telegram Mini App flows are all shipped end-to-end. Account linking across providers is implemented. OAuth (Facebook/VK), 2FA, and full session management are not implemented and are not currently planned.

---

## 🎯 Overview

The backend authenticates users via three identity providers, all converging on the same JWT access + refresh token pair:

1. **Local** (email + password) — primary identifier
2. **Google** — via Google Identity Services (ID token JWT)
3. **Telegram** — via Login Widget (web) and Mini App (`initData` from `Telegram.WebApp`)

A single user account can have any combination of these methods linked. The system uses **smart email-based linking** to automatically merge Google sign-ins with existing email accounts that share the same address.

### Key principles
- **UserId (GUID)** is the canonical primary identifier
- Email is unique when present, **nullable** (Telegram-only accounts use the synthetic email `telegram_{tgId}@telegram.local`)
- At least one auth method must remain linked to a user (no orphan accounts)
- All linked auth methods are equal — no "primary" method
- Email verification is required for local accounts; Google/Telegram are trusted via the provider's own verification

---

## 🔑 JWT Tokens

| Token | Lifetime | Where stored (web) | Algorithm |
|---|---|---|---|
| Access | 15 min | `localStorage.access_token` | HMAC-SHA256 |
| Refresh | 7 days | `localStorage.refresh_token` (web flow) **or** HttpOnly cookie `refreshToken` (HTTPS flow) | HMAC-SHA256 |

The refresh token rotates on every successful refresh — the old one is revoked and replaced.

### Access-token claims

```json
{
  "sub": "user-id-guid",
  "email": "user@example.com",
  "name": "User Name",
  "staffRole": "none|moderator|admin",
  "iat": 1234567890,
  "exp": 1234568790
}
```

`staffRole` is embedded as a custom claim so the synchronous `[RequireStaffRole]` action filter can authorize moderator/admin requests without hitting storage.

### SignalR auth

WebSocket connections can't send HTTP headers after the upgrade, so SignalR clients pass the JWT as `?access_token=<jwt>` on the `/hubs/chat` URL. `JwtBearerEvents.OnMessageReceived` (`Program.cs`) reads it off the query string for any path starting with `/hubs`.

---

## 📋 Endpoints

All under `/api/v1/auth`. All endpoints below marked **public** are rate-limited via `[EnableRateLimiting("AuthRateLimit")]` (sliding window, **20 requests / 1 min / IP**, shared bucket across every rate-limited auth endpoint). Refresh and logout are intentionally NOT rate-limited.

### Local (email + password)

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/register` | public | Create account; sends verification email |
| GET | `/registration-config` | public, unrated | `{ requireEventInvite: bool }` from appconfig |
| POST | `/login` | public | Returns access + refresh + sets HttpOnly cookie |
| POST | `/logout` | required | Revokes refresh token, clears cookie |
| POST | `/refresh` | public | Reads refresh from body or HttpOnly cookie; rotates pair |
| POST | `/verify-email?token=…` | public, unrated | GET in code, mounted at this path |
| POST | `/forgot-password` | public | Always returns success (anti-enumeration) |
| POST | `/reset-password` | public | Token + new password |
| POST | `/change-password` | required | Current + new |
| POST | `/resend-verification` | required | Stub — currently always succeeds |
| GET | `/me` | required | Current user info |
| GET | `/methods` | required | List of `AuthMethodDto` for the current account |

### Google Identity Services

The frontend uses `@react-oauth/google` to render a Google sign-in button that returns an ID token JWT. The backend verifies it against the configured Web client ID as audience.

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/google-config` | public, rate-limited | `{ clientId }` — not a secret; lets the frontend bootstrap without env var |
| POST | `/google-login` | public, rate-limited | Body `{ idToken }`. Returns `{ status: "signedIn"|"pending"|"emailConflict", auth?, ticket?, google? }` |
| POST | `/google-register` | public, rate-limited | Body `{ ticket, name, age, location, gender, bio?, inviteCode? }` — redeems pending ticket |

**`emailConflict`** is returned when the Google account email matches an existing local account but Google is not yet linked to it. The frontend prompts the user to log in with their existing password (which auto-links Google).

### Telegram Login Widget (Phase 1 — shipped Apr 19–20, 2026)

Used on the public website. The widget posts a signed payload to the page; the frontend forwards it to the backend, which verifies the HMAC against the bot token.

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/telegram-login-config` | public, rate-limited | `{ botUsername }` for the widget script |
| POST | `/telegram-login` | public, rate-limited | Body is the raw Telegram widget payload. Returns `{ status: "signedIn"|"pending", auth?, ticket?, telegram? }` |
| POST | `/telegram-register` | public, rate-limited | Redeems pending ticket + profile fields |
| POST | `/telegram-link-login` | public, rate-limited | Body `{ email, password, ticket }` — link Telegram to an existing email account in one call |
| POST | `/telegram-link` | required | Body `{ ticket }` — link Telegram to currently-authenticated account |

### Telegram Mini App (Phase 2)

For Mini App contexts inside Telegram clients. Uses `Telegram.WebApp.initData` and a different HMAC scheme from the widget (`secret_key = HMAC_SHA256("WebAppData", bot_token)`).

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/telegram-miniapp-login` | public, rate-limited | Body `{ initData }`. Returns `{ status: "signedIn"|"needsRegistration", auth?, telegram? }`. **No** ticket — Mini App renders an inline wizard and replays `initData` on the register call. |
| POST | `/telegram-miniapp-register` | public, rate-limited | Body `{ initData, name, age, location, gender, bio?, inviteCode? }` |
| POST | `/telegram-miniapp-link-login` | public, rate-limited | Body `{ initData, email, password }` — link Telegram to existing local account |

### Account management

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/attach-email` | required | Telegram-only / Google-only user requests adding email+password. Body `{ email, password }`. Sends verification email; `local` auth method only added when verification link is clicked. Returns `AttachEmailResult` enum (`Ok`/`UserNotFound`/`EmailAlreadyTaken`/`AlreadyHasLocal`/`ReservedDomain`) |

---

## 🔐 Pending-ticket flow (Google + Telegram Widget)

When the identity is **new to the system**, the verifier endpoints don't write a user row immediately. They return a short-lived `ticket` (server-signed, encodes the verified provider identity) and the frontend routes the user to a "complete your profile" screen:

- Google → `/welcome/google` (collects age/location/gender/bio + optional invite code)
- Telegram Widget → `/welcome/telegram` (same)
- Telegram Mini App → inline wizard inside the Mini App shell (no separate route — uses `Telegram.WebApp.initData` directly)

The frontend then calls the matching `*-register` endpoint with `ticket` (or `initData`) + profile fields. Only that call creates the user row.

This pattern means:
- No half-created accounts in storage if the user abandons signup
- Profile fields are collected before the user row exists
- The same `inviteCode` validation runs as for email registration
- The pending ticket is short-lived and single-use

If the invite-code gate is on (`appconfig`/`registration`/`require_event_invite = true`), the welcome screen requires a valid event invite code before completing registration. Campaign codes (negative event IDs) also pass.

---

## 🔗 Smart account linking

When the user signs in with Google **and** the Google email matches an existing account:

- **If the existing account already has Google linked** → just sign them in
- **If the existing account is local-only (email+password) and unverified-Google-conflict** → return `emailConflict`. Frontend prompts: "An account already exists with this email — sign in with your password to link Google." User submits password; backend auto-links Google as an additional `AuthMethod` and signs them in.

For Telegram linking the flow is more explicit — the user must always either:
- Use `/telegram-link-login` (give email+password + ticket in one call), or
- Be already logged in and call `/telegram-link` (just `{ ticket }`)

This is because Telegram identities don't carry email, so smart-linking has no key to match on.

### External profile photo

When Google/Telegram register or attach succeeds, `IImageService.DownloadAndUploadExternalImageAsync` fetches the provider CDN photo, resizes (max 800px, JPEG Q85), uploads to the `profile-images` Azure Blob container, and stores the blob URL on `UserEntity.ProfileImage`. Failures are silently swallowed — the user just ends up with an empty profile image rather than blocking the signup.

For `Attach*` flows the photo is only set if the user's existing `ProfileImage` is empty (we don't overwrite manually-chosen photos).

---

## 🛡️ Verification details

### Password (local)

- **Hashing**: PBKDF2-HMAC-SHA256, 100,000 iterations, random 16-byte salt per password
- **Requirements** (enforced in `AuthController.IsValidPassword`):
  - Min 8 chars
  - At least one uppercase + lowercase + digit + special (`!@#$%^&*()_+-=[]{}|;:,.<>?`)
- **Reset**: 30-min token in `authtokens` table; all refresh tokens revoked on successful reset

### Telegram Login Widget (`TelegramLoginVerifier.cs`)

```
data_check_string = sort all non-empty fields except `hash` by string.CompareOrdinal,
                    join with "\n"
secret_key        = SHA256(bot_token)
expected_hash     = HMAC_SHA256(secret_key, data_check_string)
```

Reject if `expected_hash != provided_hash` or `auth_date` is older than 24 h.

### Telegram Mini App (`TelegramWebAppVerifier.cs`)

```
secret_key        = HMAC_SHA256("WebAppData", bot_token)
data_check_string = sort initData key=value pairs by string.CompareOrdinal,
                    skip `hash`, join with "\n"
expected_hash     = HMAC_SHA256(secret_key, data_check_string)
```

Note the different secret-key derivation. Reject if hashes don't match or `auth_date` is older than 1 hour (stricter than widget — user is actively in the Mini App).

### Google ID token

`GoogleIdTokenVerifier` validates the ID token JWT:
- Signature against Google's published JWKS
- `aud` matches the configured `GOOGLE_OAUTH_CLIENT_ID` Web client ID
- `iss` is `https://accounts.google.com` or `accounts.google.com`
- `exp` is in the future, `iat` not too far in the past
- `email_verified == true`

---

## 📊 Storage schema

### `users` table

`UserEntity` — PK `user-{firstChar}`, RK `userId`. Notable fields:

```
Email, PasswordHash, Name, Age, Gender, Bio, Location, ProfileImage,
ImagesJson, PromptsJson, EmailVerified,
AuthMethodsJson      // ["local"], ["google"], ["telegram"], or any combination
TelegramUserId       // empty unless Telegram-linked
GoogleUserId         // empty unless Google-linked
InstagramHandle      // optional
StaffRole            // "none" | "moderator" | "admin"
RankOverride         // nullable; admin-set
RegistrationSourceEventId, RegistrationSourceRedeemedAtUtc   // immutable invite-source audit
ReplyCount, LikesReceived, EventsAttended, MatchCount        // activity counters used for rank
CreatedAt, UpdatedAt
```

### Reverse-lookup indexes

| Table | PK | RK | Value |
|---|---|---|---|
| `useremailindex` | normalised email | `INDEX` | `UserId` |
| `usertelegramindex` | Telegram id (string) | `INDEX` | `UserId` |
| `usergoogleindex` | Google `sub` | `INDEX` | `UserId` |

### Token tables

| Table | Purpose | Lifetime |
|---|---|---|
| `refreshtokens` | Rotating refresh tokens, hashed | 7 d |
| `authtokens` | Email-verification & password-reset tokens, hashed | 24 h (verify) / 30 min (reset) |

`UserEntity.AuthMethodsJson` is the authoritative list of linked methods used for `GET /api/v1/auth/methods`. There is **no** separate `AuthMethods` table.

---

## 🔒 Security checklist

- [x] Passwords hashed (PBKDF2 + random salt + 100k iterations)
- [x] JWT signed (HMAC-SHA256); access 15 min / refresh 7 d
- [x] Refresh-token rotation (one-time use)
- [x] HttpOnly cookie support for refresh tokens (`Secure` flag conditional on `Request.IsHttps`)
- [x] Rate limiting on all login/register/forgot-password/telegram/google endpoints (20/min/IP, shared bucket)
- [x] HTTPS in production (Cloudflare + Origin Cert on nginx)
- [x] CORS restricted (`localhost:8080`, `localhost:5173`, `localhost:3000`, `aloeve.club`, `www.aloeve.club`)
- [x] Input sanitization (`HtmlGuard` on user-facing fields)
- [x] CSRF — N/A for token-based auth (no cookies on bearer-token requests)
- [x] SQL injection — N/A (Azure Table Storage)
- [x] XSS — React auto-escapes; BB-code renderer uses no `dangerouslySetInnerHTML`
- [ ] **Account lockout** — not implemented (PB.4)
- [ ] **Secrets in Azure Key Vault** — currently env vars / `.env` file
- [ ] **localStorage XSS exposure** — refresh-token-in-cookie path supported but web client still uses localStorage flow (TD.7)
- [ ] **2FA / TOTP** — not implemented, not planned for current scope
- [ ] **Session management UI** — not implemented (no list-active-sessions endpoint)

---

## 🧪 Test coverage

Auth-related test files in `Lovecraft.UnitTests/`:

- `AuthenticationTests` — register/login/refresh/password
- `RefreshTokenTests` — rotation, revocation, expiry
- `TelegramLoginVerifierTests` — HMAC round-trip + known vectors
- `TelegramPendingFlowTests` — Login Widget pending-ticket redemption
- `TelegramMiniAppFlowTests` — initData verification, pending vs signed-in branches
- `GooglePendingFlowTests` — Google ID token verifier + pending-ticket flow
- `RateLimitingTests` — 429 response and `Retry-After` header
- `AclTests` — `[RequireStaffRole]` / `[RequirePermission]` filter integration

All tests run under `dotnet test`. Assembly-level `[CollectionBehavior(DisableTestParallelization = true)]` is required because `MockDataStore` is static.

---

## ⚙️ Configuration

```bash
# JWT
JWT_SECRET_KEY=<32+ random bytes, base64 or hex>

# Google
GOOGLE_OAUTH_CLIENT_ID=<web client id>.apps.googleusercontent.com
# or Google__ClientId in appsettings.json

# Telegram
TELEGRAM_BOT_TOKEN=<from BotFather>
TELEGRAM_BOT_USERNAME=<bot username without @>
# or Telegram__BotToken / Telegram__BotUsername in appsettings.json
# BotFather /setdomain must include aloeve.club and www.aloeve.club for the widget to render

# Email
SENDGRID_API_KEY=<sendgrid api key>          # absent → NullEmailService logs to console
FROM_EMAIL=noreply@aloeband.ru
FRONTEND_BASE_URL=https://aloeve.club        # used in verification + reset links

# Storage mode
USE_AZURE_STORAGE=true|false
AZURE_STORAGE_CONNECTION_STRING=<conn str>
AZURE_TABLE_PREFIX=                          # optional: isolate dev/test datasets
```

---

## 📚 Related docs

- **[TELEGRAM_AUTH.md](./TELEGRAM_AUTH.md)** — Telegram Login Widget + Mini App + Bot worker details
- **[GOOGLE_OAUTH_SETUP.md](./GOOGLE_OAUTH_SETUP.md)** — Google Cloud Console setup
- **[INVITES.md](./INVITES.md)** — Event invite codes used by all register endpoints
- **[AZURE_STORAGE.md](./AZURE_STORAGE.md)** — Full table schema
- **[CHAT_ARCHITECTURE.md](./CHAT_ARCHITECTURE.md)** — SignalR JWT-over-query-string detail
- **[../../../aloevera-harmony-meet/docs/FRONTEND_AUTH_GUIDE.md](../../../aloevera-harmony-meet/docs/FRONTEND_AUTH_GUIDE.md)** — frontend integration
