# Authentication & Authorization Design

**AloeVera Harmony Meet - LoveCraft Backend**

This document describes the authentication and authorization architecture for the multi-client AloeVera Harmony Meet platform.

---

## 🎯 Overview

The system supports multiple authentication methods with the ability to link multiple providers to a single user account.

### Authentication Methods

1. **Username/Password** - Standard registration with email verification
2. **OAuth 2.0** - Google, Facebook, VK, and other providers
3. **Telegram** - Integration with Telegram Mini App using Telegram User ID

### Key Principles

- **Single User Identity** - One user account can have multiple authentication methods
- **Account Linking** - Users can link additional auth methods to their existing account
- **Email as Primary Identifier** - Email is the primary way to identify and link accounts (when available)
- **JWT Tokens** - Stateless authentication using JSON Web Tokens
- **Security First** - Industry-standard security practices

---

## 🏗️ Architecture Overview

### User Identity Model

```
User (Single Identity)
├── UserId (GUID) ← PRIMARY IDENTIFIER
├── Email (nullable, unique when set)
├── EmailVerified (bool)
├── CreatedAt
└── AuthenticationMethods[] (at least one required)
    ├── Local (username/password)
    │   ├── Username
    │   ├── PasswordHash
    │   └── Salt
    ├── OAuth Providers
    │   ├── Google (ProviderId: "google", ExternalId: "...")
    │   ├── Facebook (ProviderId: "facebook", ExternalId: "...")
    │   └── VK (ProviderId: "vk", ExternalId: "...")
    └── Telegram
        ├── ProviderId: "telegram"
        └── TelegramUserId (can exist without email)
```

**Key Principles:**
- **UserId (GUID)** is the primary identifier for all users
- **Email** is unique when present, but nullable (Telegram-only users may not have email)
- **At least one auth method** must be linked to account
- **All auth methods are equal** - no "primary" method

### Authentication Flow

```
┌─────────────┐
│   Client    │
│ (Web/TG)    │
└──────┬──────┘
       │
       │ 1. Login Request
       ▼
┌─────────────────────┐
│  Auth Controller    │
│  /api/v1/auth       │
└──────┬──────────────┘
       │
       │ 2. Validate Credentials
       ▼
┌─────────────────────┐
│  Auth Service       │
│  - Validate         │
│  - Link accounts    │
└──────┬──────────────┘
       │
       │ 3. Generate JWT
       ▼
┌─────────────────────┐
│  JWT Service        │
│  - Access Token     │
│  - Refresh Token    │
└──────┬──────────────┘
       │
       │ 4. Return Tokens
       ▼
┌─────────────────────┐
│   Client            │
│   Store tokens      │
└─────────────────────┘
```

---

## 🔐 Authentication Methods

### 1. Username/Password (Local Authentication)

**Registration Flow:**

1. User provides: username, email, password
2. System validates:
   - Username is unique (case-insensitive)
   - Email is valid and unique (case-insensitive)
   - Password meets requirements (min 8 chars, number, special char)
3. System creates user account:
   - Generate UserId (GUID)
   - Hash password with BCrypt (12 rounds)
   - Generate email verification token
   - Send verification email
4. **User must verify email before accessing the system**
5. User clicks email verification link
6. Account marked as email verified
7. User can now log in and use the system

**Login Flow:**

1. User provides: username/email + password
2. System validates:
   - Find user by username OR email
   - Verify password hash
   - Check if account is active
3. Generate and return JWT tokens

**Password Requirements:**
- Minimum 8 characters
- At least one uppercase letter
- At least one lowercase letter
- At least one number
- At least one special character (!@#$%^&*()_+-=[]{}|;:,.<>?)

**Endpoints:**
```
POST /api/v1/auth/register
POST /api/v1/auth/login
POST /api/v1/auth/verify-email?token={token}
POST /api/v1/auth/forgot-password
POST /api/v1/auth/reset-password
```

### 2. OAuth 2.0 (Google, Facebook, VK)

**Registration/Login Flow:**

1. User clicks "Sign in with Google" (or other provider)
2. Frontend redirects to OAuth provider
3. User authorizes on provider's site
4. Provider redirects back with authorization code
5. Backend exchanges code for access token
6. Backend retrieves user info from provider (email, name, profile picture)
7. System checks if account exists:
   - **If email exists**: Automatically link OAuth method to existing account (smart linking)
   - **If new user**: Create new account with OAuth method (email pre-verified)
8. Generate and return JWT tokens

**Smart Account Linking:**
When a user signs in with OAuth (e.g., Google) and the email matches an existing account:
- System automatically adds the OAuth provider as a new authentication method
- No duplicate account is created
- User is informed: "Your Google account has been linked to your existing account"
- This provides seamless experience while maintaining account uniqueness

**Account Linking:**
- Smart linking: If OAuth email matches existing account → Automatically link (no duplicate account)
- If user is already logged in and adds OAuth provider → Link immediately
- Email serves as the unique identifier for automatic linking

**Endpoints:**
```
GET  /api/v1/auth/oauth/{provider}/login        # Initiate OAuth flow
GET  /api/v1/auth/oauth/{provider}/callback     # OAuth callback
POST /api/v1/auth/oauth/{provider}/link         # Link OAuth to existing account
DELETE /api/v1/auth/oauth/{provider}/unlink     # Unlink OAuth provider
```

**Supported Providers:**
- Google OAuth 2.0
- Facebook Login
- VK OAuth 2.0
- (Extensible for future providers)

### 3. Telegram Mini App Authentication

**Registration/Login Flow:**

1. User opens Telegram Mini App
2. Telegram provides `initData` with user information
3. Mini App sends `initData` to backend
4. Backend validates `initData` hash (using bot token)
5. Backend extracts Telegram User ID
6. System checks if Telegram account exists:
   - **If exists**: Log in user
   - **If new**: Create new account with Telegram auth method (no email required)
7. Generate and return JWT tokens

**Telegram Users Without Email:**
- Telegram-only users can use the system without providing an email
- Email is optional for Telegram authentication
- If user later wants to add username/password or OAuth, they can add email at that time

**Telegram InitData Validation:**
```csharp
// Validate initData hash using HMAC-SHA256
// Secret key = HMAC-SHA256(bot_token, "WebAppData")
// Compare computed hash with provided hash
```

**Account Linking:**
- Telegram users can later add email/password or OAuth methods
- To add email/password: User must provide and verify email first
- To add OAuth: If OAuth provides email, it becomes the user's email (with verification)

**Endpoints:**
```
POST /api/v1/auth/telegram/login     # Login with Telegram initData
POST /api/v1/auth/telegram/link      # Link Telegram to existing account
DELETE /api/v1/auth/telegram/unlink  # Unlink Telegram account
```

---

## 🎫 JWT Token Strategy

### Access Token

**Purpose:** Short-lived token for API authentication

**Lifetime:** 15 minutes

**Claims:**
```json
{
  "sub": "user-id-guid",
  "email": "user@example.com",
  "username": "johndoe",
  "role": "user",
  "iat": 1234567890,
  "exp": 1234569690
}
```

**Storage:** 
- Web: Memory (not localStorage for security)
- Telegram: Secure storage if available

### Refresh Token

**Purpose:** Long-lived token to obtain new access tokens

**Lifetime:** 7 days

**Storage:**
- Web: HttpOnly secure cookie
- Telegram: Secure storage

**Rotation:** Refresh tokens rotate on use (one-time use)

### Token Refresh Flow

```
Client sends expired access token
    ↓
Middleware detects 401
    ↓
Client sends refresh token to /api/v1/auth/refresh
    ↓
Backend validates refresh token
    ↓
Backend generates new access + refresh tokens
    ↓
Client stores new tokens
```

---

## 🔗 Account Linking Strategy

### Linking Rules

1. **Smart Email-based Linking:**
   - If OAuth provider returns email that matches existing account → **Automatically link** (no duplicate account)
   - System notifies user: "Your [Provider] account has been linked"
   - This provides seamless UX while maintaining account uniqueness

2. **Manual Linking:**
   - Logged-in user can link additional auth methods from settings
   - User must authenticate with new method to prove ownership

3. **Telegram Linking:**
   - Telegram users without email can link OAuth (email will be added from OAuth)
   - To add username/password: Must provide and verify email first

### Linking Scenarios

**Scenario 1: User has username/password, wants to add Google**
```
1. User is logged in
2. User clicks "Link Google Account" in settings
3. User authenticates with Google
4. System verifies email matches (if available)
5. Google auth method added to user account
6. User can now log in with either method
```

**Scenario 2: User signs up with Google, later wants to add password**
```
1. User is logged in (via Google)
2. User goes to settings → "Add Password"
3. User provides username + password
4. System validates and saves password hash
5. User can now log in with username/password OR Google
```

**Scenario 3: User with email/password signs in with Google (same email) - Smart Linking**
```
1. User clicks "Sign in with Google"
2. Backend detects email exists in system
3. Backend automatically links Google to existing account
4. Backend logs user in with existing account
5. Backend returns success notification:
   "Your Google account has been linked to your existing account.
    You can now sign in with either method."
6. User is logged in (no duplicate account created)
```

**Scenario 4: Telegram user wants to add email/password**
```
1. User logs in via Telegram Mini App
2. System detects user has no email
3. Prompt user to add email in settings
4. User provides email → Send verification
5. After verification, user can add password or OAuth
```

### Unlinking Rules

- User must have at least **one active authentication method**
- Cannot unlink the last remaining method
- Unlinking requires password confirmation (if password exists)

---

## 🛡️ Security Considerations

### Password Security

- **Hashing Algorithm:** BCrypt with cost factor 12
- **Salt:** Random 128-bit salt per password
- **Validation:** Check against common passwords list
- **History:** Optional - prevent reuse of last 5 passwords

### Token Security

- **Access Token:**
  - Short-lived (30 minutes)
  - Signed with HMAC-SHA256
  - Secret key stored in Azure Key Vault
- **Refresh Token:**
  - Stored in database (hashed)
  - One-time use (rotates on refresh)
  - Can be revoked (logout, password change, etc.)

### OAuth Security

- **State Parameter:** CSRF protection
- **PKCE:** For public clients (Telegram Mini App)
- **Scope Limitation:** Request only necessary permissions
- **Token Validation:** Always validate OAuth tokens server-side

### Telegram Security

- **InitData Validation:** Verify HMAC signature
- **Timestamp Check:** Reject old initData (> 5 minutes)
- **Bot Token Protection:** Never expose in client code

### Rate Limiting

- **Login Attempts:** 5 per 15 minutes per IP
- **Registration:** 3 per hour per IP
- **Password Reset:** 3 per hour per email
- **Token Refresh:** 10 per minute per user

### Account Protection

- **Account Lockout:** After 10 failed login attempts
- **Email Verification:** Required before certain actions (optional initially)
- **2FA Support:** Future consideration
- **Session Management:** Track active sessions, allow revocation

---

## 📊 Database Schema

### Users Table (Azure Table Storage)

```
PartitionKey: "USER"
RowKey: {UserId}
Properties:
  - UserId (GUID)
  - Email (string, nullable, indexed)
  - EmailVerified (bool)
  - Username (string, nullable)
  - CreatedAt (DateTime)
  - UpdatedAt (DateTime)
  - IsActive (bool)
  - LastLoginAt (DateTime)
```

### AuthMethods Table

```
PartitionKey: {UserId}
RowKey: {ProviderId}_{ExternalId}
Properties:
  - UserId (GUID)
  - Provider (string: "local", "google", "facebook", "vk", "telegram")
  - ExternalId (string: provider's user ID, or username for local)
  - PasswordHash (string, nullable)
  - PasswordSalt (string, nullable)
  - LinkedAt (DateTime)
  - LastUsedAt (DateTime)
```

### RefreshTokens Table

```
PartitionKey: {UserId}
RowKey: {TokenId}
Properties:
  - UserId (GUID)
  - TokenHash (string)
  - ExpiresAt (DateTime)
  - CreatedAt (DateTime)
  - RevokedAt (DateTime, nullable)
  - ReplacedByTokenId (GUID, nullable)
```

### EmailVerificationTokens Table

```
PartitionKey: "EMAIL_VERIFY"
RowKey: {Token}
Properties:
  - UserId (GUID)
  - Email (string)
  - Token (GUID)
  - ExpiresAt (DateTime)
  - UsedAt (DateTime, nullable)
```

### PasswordResetTokens Table

```
PartitionKey: "PASSWORD_RESET"
RowKey: {Token}
Properties:
  - UserId (GUID)
  - Token (GUID)
  - ExpiresAt (DateTime)
  - UsedAt (DateTime, nullable)
```

---

## 📋 API Endpoints

### Authentication

```
POST   /api/v1/auth/register
POST   /api/v1/auth/login
POST   /api/v1/auth/logout
POST   /api/v1/auth/refresh
GET    /api/v1/auth/me
POST   /api/v1/auth/verify-email
POST   /api/v1/auth/resend-verification
POST   /api/v1/auth/forgot-password
POST   /api/v1/auth/reset-password
```

### OAuth

```
GET    /api/v1/auth/oauth/{provider}/login
GET    /api/v1/auth/oauth/{provider}/callback
POST   /api/v1/auth/oauth/{provider}/link
DELETE /api/v1/auth/oauth/{provider}/unlink
```

### Telegram

```
POST   /api/v1/auth/telegram/login
POST   /api/v1/auth/telegram/link
DELETE /api/v1/auth/telegram/unlink
```

### Account Management

```
GET    /api/v1/auth/methods              # List linked auth methods
POST   /api/v1/auth/methods/password     # Add/change password
DELETE /api/v1/auth/methods/{provider}   # Unlink auth method
GET    /api/v1/auth/sessions             # List active sessions
DELETE /api/v1/auth/sessions/{id}        # Revoke session
POST   /api/v1/auth/change-email         # Change email
```

---

## 🔄 User Flows

### Flow 1: New User Registration (Username/Password)

```
1. User visits registration page
2. User enters: username, email, password
3. Frontend validates input
4. Frontend POST /api/v1/auth/register
5. Backend creates user with "local" auth method
6. Backend sends verification email
7. Backend returns JWT tokens
8. User is logged in (email verification optional)
9. User clicks verification link in email
10. Backend marks email as verified
```

### Flow 2: New User Registration (Google OAuth)

```
1. User clicks "Sign in with Google"
2. Frontend redirects to /api/v1/auth/oauth/google/login
3. Backend redirects to Google OAuth
4. User authenticates with Google
5. Google redirects to /api/v1/auth/oauth/google/callback
6. Backend retrieves user info from Google
7. Backend creates user with "google" auth method
8. Backend returns JWT tokens
9. Frontend stores tokens and redirects to dashboard
```

### Flow 3: Existing User Login (Any Method)

```
1. User enters credentials OR clicks OAuth button
2. Frontend sends credentials to appropriate endpoint
3. Backend validates credentials
4. Backend generates JWT tokens
5. Backend updates LastLoginAt
6. Backend returns tokens
7. Frontend stores tokens
8. User is logged in
```

### Flow 4: Linking OAuth to Existing Account

```
1. User is logged in (with access token)
2. User goes to Settings → "Link Google Account"
3. Frontend GET /api/v1/auth/oauth/google/login?link=true
4. Backend initiates OAuth with state parameter
5. User authenticates with Google
6. Backend receives callback
7. Backend verifies user is logged in (from state)
8. Backend checks if Google email matches user's email
9. Backend adds "google" auth method to user
10. Backend returns success
11. User can now log in with Google
```

### Flow 5: Password Reset

```
1. User clicks "Forgot password"
2. User enters email
3. Frontend POST /api/v1/auth/forgot-password
4. Backend generates reset token
5. Backend sends reset email
6. User clicks link in email
7. Frontend shows reset password form
8. User enters new password
9. Frontend POST /api/v1/auth/reset-password
10. Backend validates token
11. Backend updates password hash
12. Backend revokes all refresh tokens
13. User must log in again
```

---

## 🚀 Implementation Phases

### Phase 1: Local Authentication (MVP)
- Username/password registration
- Email verification
- Login/logout
- JWT access & refresh tokens
- Password reset flow

### Phase 2: OAuth Integration
- Google OAuth
- Facebook OAuth
- VK OAuth
- Account linking UI

### Phase 3: Telegram Integration
- Telegram initData validation
- Telegram login
- Account linking for Telegram users

### Phase 4: Advanced Features
- 2FA (TOTP)
- Session management
- Security notifications
- Account activity log

---

## 🔧 Configuration

### Environment Variables

```bash
# JWT Configuration
JWT_SECRET_KEY=<secret-key-from-azure-key-vault>
JWT_ACCESS_TOKEN_LIFETIME_MINUTES=15
JWT_REFRESH_TOKEN_LIFETIME_DAYS=7

# OAuth - Google
GOOGLE_CLIENT_ID=<google-client-id>
GOOGLE_CLIENT_SECRET=<google-client-secret>
GOOGLE_REDIRECT_URI=https://api.aloemore.ru/api/v1/auth/oauth/google/callback

# OAuth - Facebook
FACEBOOK_APP_ID=<facebook-app-id>
FACEBOOK_APP_SECRET=<facebook-app-secret>
FACEBOOK_REDIRECT_URI=https://api.aloemore.ru/api/v1/auth/oauth/facebook/callback

# OAuth - VK
VK_CLIENT_ID=<vk-client-id>
VK_CLIENT_SECRET=<vk-client-secret>
VK_REDIRECT_URI=https://api.aloemore.ru/api/v1/auth/oauth/vk/callback

# Telegram
TELEGRAM_BOT_TOKEN=<telegram-bot-token>

# Email Service
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USERNAME=<email>
SMTP_PASSWORD=<password>
FROM_EMAIL=noreply@aloemore.ru

# Frontend URLs (for redirects)
WEB_APP_URL=https://harmony.aloemore.ru
TELEGRAM_APP_URL=https://t.me/aloevera_bot/app
```

---

## ✅ Design Decisions (Confirmed)

Based on requirements, the following decisions have been finalized:

1. **Primary Identifier:**
   - ✅ UserId (GUID) is the primary identifier
   - ✅ Email is unique when present (nullable for Telegram-only users)

2. **Email Verification:**
   - ✅ **Required before system access** for username/password registration
   - ✅ OAuth accounts are pre-verified (provider already verified email)
   - ✅ Telegram-only users don't need email (optional)

3. **Smart Account Linking:**
   - ✅ If OAuth email matches existing account → **Automatically link** (no duplicate)
   - ✅ User is notified about the linking
   - ✅ Prevents duplicate accounts with same email

4. **Telegram Users:**
   - ✅ Can use system without email
   - ✅ Email becomes required only when adding username/password auth
   - ✅ Can link OAuth (email will be added from OAuth provider)

5. **Password Requirements:**
   - ✅ Minimum 8 characters
   - ✅ At least one uppercase, lowercase, number, and special character

6. **Token Lifetime:**
   - ✅ Access Token: 15 minutes
   - ✅ Refresh Token: 7 days

7. **Auth Methods:**
   - ✅ All methods are equal (no primary method)
   - ✅ User must have at least one method linked
   - ✅ Can unlink methods as long as one remains

8. **Account Unlinking:**
   - ✅ Users can unlink auth methods
   - ✅ Must maintain at least one active method

---

## 🔐 Security Checklist

- [ ] Passwords hashed with BCrypt (cost 12)
- [ ] JWT tokens signed and validated
- [ ] Refresh tokens stored hashed in database
- [ ] Refresh token rotation implemented
- [ ] Rate limiting on auth endpoints
- [ ] Account lockout after failed attempts
- [ ] OAuth state parameter validation
- [ ] Telegram initData signature verification
- [ ] HTTPS only in production
- [ ] HttpOnly cookies for refresh tokens
- [ ] CORS properly configured
- [ ] Secrets stored in Azure Key Vault
- [ ] Input validation on all endpoints
- [ ] SQL injection prevention (N/A - NoSQL)
- [ ] XSS prevention
- [ ] CSRF protection

---

**Document Version:** 1.1  
**Last Updated:** 2026-02-18  
**Status:** ✅ Finalized - Ready for Implementation
