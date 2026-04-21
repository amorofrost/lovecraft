# Telegram Authentication

**Status**: Phase 1 (Web Login Widget) shipped Apr 19–20 2026 (commits `da1366f` / `a681930`). Phase 2 (Mini App) tracked as MCF.17.

This doc covers the full surface area: the current Web Login Widget flow, the Telegram bot worker, and the planned Mini App (`initData`) flow. All three share a single `IAuthService` surface so the frontend, Mini App shell, and future admin tools see one consistent JWT pair.

---

## 1. Goals

- Allow a user to sign in with one tap from:
  - the public website (Login Widget)
  - inside Telegram itself (Mini App — MCF.17)
- Produce the **same JWT access/refresh token pair** as email/password login, so every downstream service (SignalR, matching, chat, forum, events) works identically.
- Keep the bot worker as a lightweight long-poller initially; elevate to a command/notification bus later.
- Never require the user to give us an email address just to sign in with Telegram.

## 2. Non-goals (for now)

- Two-factor auth via Telegram code messages.
- Cross-device session linking (QR from desktop → confirm on phone).
- Event / match notifications through the bot (planned but not in this doc).

---

## 3. High-level flows

```
┌──────────────┐  widget   ┌────────────────┐  POST /telegram-login   ┌──────────┐
│  web browser │──────────▶│ telegram.org   │────────────────────────▶│ backend  │
│  (Welcome)   │  button   │ auth UI        │  { id, hash, auth_date} │  API     │
└──────────────┘           └────────────────┘                         └─────┬────┘
                                                                             │ HMAC
                                                                             ▼
                                                           verify → find/create → JWT

┌──────────────┐  Mini App ┌────────────────┐  POST /telegram-miniapp-login ┌──────────┐
│ Telegram app │──────────▶│ web view        │──────────────────────────────▶│ backend │
│ (WebApp)     │  iframe   │ /telegram       │  { initData }                 │  API    │
└──────────────┘           └────────────────┘                                └─────────┘

┌──────────────┐                     ┌──────────────────┐
│ Telegram app │──── /start ────────▶│ bot worker (LP)  │  (future: deep-link start params,
│              │                     │ Lovecraft.Tele.. │   command menu, notifications)
└──────────────┘                     └──────────────────┘
```

All three flows end the same way: `AzureAuthService` (or `MockAuthService`) returns `AuthResponseDto` (access token + refresh token + user).

---

## 4. Phase 1: Web Login Widget (shipped)

### 4.1 Configuration

| Source                              | Keys                                                    |
|-------------------------------------|---------------------------------------------------------|
| `appsettings.json` / env            | `Telegram__BotToken`, `Telegram__BotUsername`           |
| `.env` (back-compat)                | `TELEGRAM_BOT_TOKEN`, `TELEGRAM_BOT_USERNAME`           |
| Frontend env (optional)             | `VITE_TELEGRAM_BOT_USERNAME` (bypasses config fetch)    |

Bound via `TelegramAuthOptions` + `PostConfigure` shim in `Lovecraft.Backend/Program.cs:158-168`. BotFather `/setdomain` must include `aloeve.club` and `www.aloeve.club`, otherwise the widget button renders disabled.

### 4.2 Frontend component

`src/components/TelegramLoginWidget.tsx`:

1. Fetches bot username from `GET /api/v1/auth/telegram-login-config` (or reads `VITE_TELEGRAM_BOT_USERNAME`).
2. Injects `<script src="https://telegram.org/js/telegram-widget.js?22" data-telegram-login data-onauth="onTelegramAuth(user)" …>` into its container.
3. Registers `window.onTelegramAuth` — called by Telegram's iframe with the signed payload.
4. Calls `authApi.telegramLogin(user)` → stores tokens via `apiClient`, toasts success, navigates to `/friends`.

### 4.3 Backend verification

`Lovecraft.Backend/Auth/TelegramLoginVerifier.cs`:

- Rejects payloads older than `MaxAuthAge = 24h`.
- Builds `data_check_string` from all non-empty fields (except `hash`) sorted by `string.CompareOrdinal`, joined by `\n`.
- Computes `HMAC-SHA256(key = SHA256(bot_token), data = data_check_string)`.
- Constant-time compare (via .NET `string.Equals` — see *Fixes* section for note on timing).

### 4.4 Account provisioning

`AzureAuthService.TelegramLoginAsync` (`Services/Azure/AzureAuthService.cs:212-337`):

1. Look up Telegram id in `usertelegramindex` (PK = id, RK = "INDEX" → `UserId`).
2. If found → load user row → issue JWT.
3. If not found → create `UserEntity` with:
   - `Email = telegram_{tgId}@telegram.local` (sentinel — never deliverable)
   - `EmailVerified = true` (no inbox exists, but account is considered trusted via Telegram)
   - `AuthMethodsJson = ["telegram"]`
   - `TelegramUserId = tgId` (denormalized)
   - Random 48-byte password hash (account is password-inaccessible)
   - `ProfileImage = photo_url` if provided
   - Defaults: Age=18, Location="Telegram", Gender=PreferNotToSay, Bio=""
4. Upsert user row + email-index row + telegram-index row in parallel, rolled back on failure.

### 4.5 Storage

- **`users`** — `UserEntity` gains `TelegramUserId` field (nullable, string).
- **`useremailindex`** — reused for the synthetic email (keeps `Email` column globally unique).
- **`usertelegramindex`** — new table; PK = Telegram id (string), RK = `"INDEX"`, value = `UserId`. Reverse lookup for logins.

### 4.6 JWT issuance

Identical to email login: same `IJwtService.GenerateAccessToken(id, email, name, staffRole)` and refresh-token table write. `staffRole` is read from the user row (or `MockDataStore.UserStaffRoles` in mock mode), so Telegram users can be promoted the same way as any other account.

---

## 5. Phase 2: Telegram Mini App (MCF.17 — planned)

The Mini App is a WebView rendered inside Telegram clients. The SDK (`telegram-web-app.js`) hands the page a signed `initData` query string on load. **This is a different signature scheme from the Login Widget** — same bot token, but the secret key uses a literal `"WebAppData"` HMAC prefix.

### 5.1 Shell entry point

Already stubbed at `public/telegram/index.html`. Future implementation: mount a React entry (`miniapp.html` / `src/miniapp/main.tsx`) that reuses existing routes (Friends, Talks, Chat) but respects:

- `Telegram.WebApp.themeParams` → maps to our `aloe-*` CSS variables so the app matches the user's dark/light Telegram theme.
- `Telegram.WebApp.BackButton` → drives `useNavigate(-1)` instead of the browser back button.
- `Telegram.WebApp.MainButton` → optional replacement for the primary in-page CTA.
- `viewportHeight` / `safeArea*` → Tailwind `env(safe-area-inset-*)`.

### 5.2 Verification

New `TelegramWebAppVerifier` (to be added next to `TelegramLoginVerifier`):

```
secret_key    = HMAC_SHA256(key = "WebAppData", data = bot_token)
data_check    = alphabetically sorted key=value pairs from initData
                (except `hash`), joined by '\n'
expected_hash = HMAC_SHA256(key = secret_key, data = data_check).hex()
```

Reject if:
- `expected_hash != provided_hash`
- `auth_date` older than 1h (stricter than widget — the user is actively in the app)
- `user` JSON is missing

### 5.3 New endpoint

```
POST /api/v1/auth/telegram-miniapp-login
{
  "initData": "<raw query string from Telegram.WebApp.initData>"
}
```

Returns the same `AuthResponseDto`. DTO already stubbed at `Lovecraft.Common/DTOs/Auth/AuthDtos.cs:TelegramWebAppInitDataDto`.

Service method: `IAuthService.TelegramMiniAppLoginAsync(string initData)` — shares the provisioning path in §4.4 with `TelegramLoginAsync` by delegating to a private `ProvisionOrLoadFromTelegramAsync(long id, string firstName, string? lastName, string? username, string? photoUrl)`. Both flows emit the same JWT.

### 5.4 Start parameters

`/start payload` and `?startapp=payload` must be parseable:

- `link_{guid}` — one-time linking code generated by the web UI when a logged-in user wants to attach Telegram to an existing email account. Bot looks the guid up in a new `telegramlinkrequests` table (TTL 10 min), links user ID → Telegram ID, replies with "linked ✅".
- `event_{eventId}` — opens the event page (deep link).
- `user_{userId}` — opens a profile (deep link).
- `invite_{code}` — opens registration with pre-filled invite code.

Parsing happens in both places (bot worker for `/start`, Mini App JS for `startapp`) and is routed through a single `TelegramStartPayload` parser in `Lovecraft.Common`.

### 5.5 Bot command menu

Bot worker calls `SetMyCommands` on startup with:

```
start   — Open AloeVera Harmony Meet
app     — Open the Mini App
help    — Help & support
```

And sets a menu button via `SetChatMenuButton(new MenuButtonWebApp { Url = "https://aloeve.club/miniapp" })` so every user sees the Mini App entry.

---

## 6. Unified auth surface

```
IAuthService
├── RegisterAsync(email/password)
├── LoginAsync(email/password)
├── TelegramLoginAsync(TelegramLoginRequestDto)          ← Phase 1 (shipped)
├── TelegramMiniAppLoginAsync(string initData)           ← Phase 2
├── RefreshTokenAsync(refreshToken)
├── LinkTelegramToAccountAsync(userId, linkPayload)      ← Phase 2
└── …
```

Both mock and Azure implementations share the provisioning helper.

Account linking (existing email account → Telegram): user clicks "Link Telegram" in Settings → backend issues a short-lived `link_{guid}` code → user opens `https://t.me/{bot}?start=link_{guid}` → bot worker receives `/start link_{guid}` → calls backend `/api/v1/auth/telegram-link-confirm` with the Telegram ID from `message.From.Id` → backend writes the `usertelegramindex` row pointing at the existing user, appends `"telegram"` to `AuthMethodsJson`. The user now has two sign-in methods.

Same table (`usertelegramindex`) serves both paths.

---

## 7. Operational concerns

### CSP

Current CSP (nginx.conf:104) allows `'unsafe-eval'` and `'unsafe-inline'` globally with `http: https:` fallback — required by `telegram-widget.js`. Tighten by:

- Scoping `script-src` to `'self' https://telegram.org https://*.telegram.org https://oauth.telegram.org 'unsafe-inline' 'unsafe-eval'` (drop the `http: https:` wildcard).
- Adding `frame-src https://oauth.telegram.org https://telegram.org` for the auth popup.
- Mini App shell will also need `frame-ancestors https://telegram.org https://web.telegram.org https://*.telegram.org` so Telegram can embed it.

### Rate limiting

`telegram-login` is currently **not** rate-limited. Add `[EnableRateLimiting("AuthRateLimit")]` — even with valid HMAC, a compromised widget payload remains replayable for `MaxAuthAge`. See *Fixes* §3.1.

### Profile completion

Telegram sign-up skips all profile fields we use for matching. New Telegram users should be routed to a minimal profile wizard (age, gender, location, bio) before `/friends`. Backend signals this with a new `profileComplete: false` boolean on `UserInfo` (derived from Age > 0 && Gender != "PreferNotToSay" && !string.IsNullOrEmpty(Location) && Location != "Telegram").

### Bot worker resilience

Use `IOptions<TelegramAuthOptions>` (same binding as backend) instead of reading `TELEGRAM_BOT_TOKEN` directly — otherwise users who only set `Telegram__BotToken` get a silent no-op bot.

---

## 8. Testing

Already present: `TelegramLoginVerifierTests` (round-trip, wrong hash, known vector).

Missing: service-level tests for `TelegramLoginAsync` (first signup, returning user, invalid hash, stale auth_date, synthetic-email collision, missing BotToken) — both against the in-memory mock and the Azurite-backed Azure service. Add to `Lovecraft.UnitTests` in the same style as `AzureAuthServiceTests`.

---

## 9. File map (Phase 1, shipped)

| File | Role |
|------|------|
| `Lovecraft.Backend/Auth/TelegramLoginVerifier.cs` | HMAC verification |
| `Lovecraft.Backend/Configuration/TelegramAuthOptions.cs` | Bound config |
| `Lovecraft.Backend/Controllers/V1/AuthController.cs` | `GET telegram-login-config` + `POST telegram-login` |
| `Lovecraft.Backend/Services/IAuthService.cs` | `TelegramLoginAsync` |
| `Lovecraft.Backend/Services/Azure/AzureAuthService.cs` | Prod provisioning |
| `Lovecraft.Backend/Services/MockAuthService.cs` | Mock provisioning |
| `Lovecraft.Backend/Storage/Entities/UserEntity.cs` | `TelegramUserId` field |
| `Lovecraft.Backend/Storage/Entities/UserTelegramIndexEntity.cs` | Reverse index |
| `Lovecraft.Backend/Storage/TableNames.cs` | `UserTelegramIndex` |
| `Lovecraft.Common/DTOs/Auth/AuthDtos.cs` | `TelegramLoginRequestDto`, `TelegramLoginConfigDto`, `TelegramWebAppInitDataDto` (stub) |
| `Lovecraft.TelegramBot/*` | Worker project (Telegram.Bot 22.4.4, net10.0) |
| `Dockerfile.telegram-bot` | Bot container image |
| `docker-compose.yml` (frontend repo) | `telegram-bot` service |
| `nginx.conf` (frontend repo) | CSP adjustments for widget |
| `public/telegram/index.html` (frontend repo) | Mini App shell stub |
| `src/components/TelegramLoginWidget.tsx` | Widget renderer |
| `src/pages/Welcome.tsx` | Integration point |
| `src/services/api/authApi.ts` | `getTelegramLoginConfig`, `telegramLogin` |
| `src/contexts/LanguageContext.tsx` | `auth.telegram` ru/en |

