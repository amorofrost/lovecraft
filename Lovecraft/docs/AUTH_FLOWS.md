# Authentication Flow Diagrams

Visual representation of authentication flows for AloeVera Harmony Meet.

---

## 1️⃣ Username/Password Registration Flow

```
┌─────────┐                                    ┌─────────┐
│  User   │                                    │ Backend │
└────┬────┘                                    └────┬────┘
     │                                              │
     │ 1. Fill registration form                   │
     │    (username, email, password)              │
     │                                              │
     │ 2. POST /api/v1/auth/register               │
     │─────────────────────────────────────────────>│
     │                                              │
     │                                              │ 3. Validate input
     │                                              │ 4. Hash password (BCrypt)
     │                                              │ 5. Create user record
     │                                              │ 6. Create auth method (local)
     │                                              │ 7. Generate JWT tokens
     │                                              │ 8. Send verification email
     │                                              │
     │ 9. Return tokens + user info                │
     │<─────────────────────────────────────────────│
     │                                              │
     │ 10. Store tokens, redirect to app           │
     │                                              │
     │                                              │
     │ ... user receives email ...                 │
     │                                              │
     │ 11. Click verification link                 │
     │                                              │
     │ 12. GET /api/v1/auth/verify-email?token=... │
     │─────────────────────────────────────────────>│
     │                                              │
     │                                              │ 13. Validate token
     │                                              │ 14. Mark email as verified
     │                                              │
     │ 15. Email verified successfully!            │
     │<─────────────────────────────────────────────│
     │                                              │
```

---

## 2️⃣ Username/Password Login Flow

```
┌─────────┐                                    ┌─────────┐
│  User   │                                    │ Backend │
└────┬────┘                                    └────┬────┘
     │                                              │
     │ 1. Enter email/username + password          │
     │                                              │
     │ 2. POST /api/v1/auth/login                  │
     │─────────────────────────────────────────────>│
     │                                              │
     │                                              │ 3. Find user by email/username
     │                                              │ 4. Verify password hash
     │                                              │ 5. Check account status
     │                                              │ 6. Generate JWT tokens
     │                                              │ 7. Update LastLoginAt
     │                                              │
     │ 8. Return tokens + user info                │
     │<─────────────────────────────────────────────│
     │                                              │
     │ 9. Store tokens, redirect to dashboard      │
     │                                              │
```

---

## 3️⃣ OAuth Registration/Login Flow (Google Example)

```
┌──────┐           ┌─────────┐        ┌────────┐        ┌────────┐
│ User │           │ Frontend│        │Backend │        │ Google │
└──┬───┘           └────┬────┘        └───┬────┘        └───┬────┘
   │                    │                  │                 │
   │ 1. Click "Sign in with Google"       │                 │
   │───────────────────>│                  │                 │
   │                    │                  │                 │
   │                    │ 2. Redirect to   │                 │
   │                    │    /oauth/google/login             │
   │                    │─────────────────>│                 │
   │                    │                  │                 │
   │                    │                  │ 3. Redirect to  │
   │                    │                  │    Google OAuth │
   │                    │                  │────────────────>│
   │                    │                  │                 │
   │ 4. Google login page                  │                 │
   │<──────────────────────────────────────────────────────────│
   │                    │                  │                 │
   │ 5. Authorize app   │                  │                 │
   │─────────────────────────────────────────────────────────>│
   │                    │                  │                 │
   │                    │                  │ 6. Return code  │
   │                    │                  │<────────────────│
   │                    │                  │                 │
   │                    │                  │ 7. Exchange code│
   │                    │                  │    for token    │
   │                    │                  │────────────────>│
   │                    │                  │                 │
   │                    │                  │ 8. Return token │
   │                    │                  │    + user info  │
   │                    │                  │<────────────────│
   │                    │                  │                 │
   │                    │                  │ 9. Check if user exists
   │                    │                  │    by email
   │                    │                  │ 10. Create/link account
   │                    │                  │ 11. Generate JWT tokens
   │                    │                  │                 │
   │                    │ 12. Redirect with tokens           │
   │                    │<─────────────────│                 │
   │                    │                  │                 │
   │ 13. Store tokens, redirect to app    │                 │
   │<───────────────────│                  │                 │
   │                    │                  │                 │
```

---

## 4️⃣ Telegram Mini App Login Flow

```
┌─────────┐         ┌──────────┐         ┌─────────┐
│Telegram │         │ Mini App │         │ Backend │
│  User   │         │(Frontend)│         │         │
└────┬────┘         └────┬─────┘         └────┬────┘
     │                   │                     │
     │ 1. Open Mini App  │                     │
     │──────────────────>│                     │
     │                   │                     │
     │                   │ 2. Telegram provides│
     │                   │    initData with    │
     │                   │    user info        │
     │<──────────────────│                     │
     │                   │                     │
     │                   │ 3. POST /auth/telegram/login
     │                   │     { initData: "..." }
     │                   │────────────────────>│
     │                   │                     │
     │                   │                     │ 4. Validate initData
     │                   │                     │    signature (HMAC)
     │                   │                     │ 5. Extract Telegram ID
     │                   │                     │ 6. Find/create user
     │                   │                     │ 7. Generate JWT tokens
     │                   │                     │
     │                   │ 8. Return tokens    │
     │                   │<────────────────────│
     │                   │                     │
     │                   │ 9. Store in Telegram│
     │                   │    secure storage   │
     │                   │                     │
```

---

## 5️⃣ Account Linking Flow (Add OAuth to Existing Account)

```
┌──────┐           ┌─────────┐        ┌────────┐        ┌────────┐
│ User │           │ Frontend│        │Backend │        │ Google │
└──┬───┘           └────┬────┘        └───┬────┘        └───┬────┘
   │                    │                  │                 │
   │ (User is already logged in with       │                 │
   │  username/password)                   │                 │
   │                    │                  │                 │
   │ 1. Go to Settings > Link Accounts     │                 │
   │───────────────────>│                  │                 │
   │                    │                  │                 │
   │ 2. Click "Link Google"                │                 │
   │───────────────────>│                  │                 │
   │                    │                  │                 │
   │                    │ 3. Redirect with access token      │
   │                    │    /oauth/google/link             │
   │                    │─────────────────>│                 │
   │                    │                  │                 │
   │                    │                  │ 4. Save user ID │
   │                    │                  │    in session   │
   │                    │                  │                 │
   │                    │                  │ 5. Redirect to  │
   │                    │                  │    Google OAuth │
   │                    │                  │────────────────>│
   │                    │                  │                 │
   │ 6. Google authorization              │                 │
   │<──────────────────────────────────────────────────────────│
   │ (authorize...)                        │                 │
   │─────────────────────────────────────────────────────────>│
   │                    │                  │                 │
   │                    │                  │ 7. Callback with│
   │                    │                  │    auth code    │
   │                    │                  │<────────────────│
   │                    │                  │                 │
   │                    │                  │ 8. Get Google user info
   │                    │                  │ 9. Verify email matches
   │                    │                  │    current user (optional)
   │                    │                  │ 10. Add "google" auth
   │                    │                  │     method to user
   │                    │                  │                 │
   │                    │ 11. Redirect: Success!             │
   │                    │<─────────────────│                 │
   │                    │                  │                 │
   │ 12. Show "Google linked successfully!"│                 │
   │<───────────────────│                  │                 │
   │                    │                  │                 │
```

---

## 6️⃣ Token Refresh Flow

```
┌─────────┐                                    ┌─────────┐
│Frontend │                                    │ Backend │
└────┬────┘                                    └────┬────┘
     │                                              │
     │ 1. API request with access token            │
     │─────────────────────────────────────────────>│
     │                                              │
     │                                              │ 2. Validate token
     │                                              │ 3. Token expired!
     │                                              │
     │ 4. Return 401 Unauthorized                  │
     │<─────────────────────────────────────────────│
     │                                              │
     │ 5. POST /api/v1/auth/refresh                │
     │    (with refresh token in cookie)           │
     │─────────────────────────────────────────────>│
     │                                              │
     │                                              │ 6. Validate refresh token
     │                                              │ 7. Check if revoked
     │                                              │ 8. Generate new tokens
     │                                              │ 9. Revoke old refresh token
     │                                              │
     │ 10. Return new access + refresh tokens      │
     │<─────────────────────────────────────────────│
     │                                              │
     │ 11. Store new access token                  │
     │ 12. Retry original API request              │
     │─────────────────────────────────────────────>│
     │                                              │
     │                                              │ 13. Process request
     │                                              │
     │ 14. Return data                             │
     │<─────────────────────────────────────────────│
     │                                              │
```

---

## 7️⃣ Password Reset Flow

```
┌─────────┐                                    ┌─────────┐
│  User   │                                    │ Backend │
└────┬────┘                                    └────┬────┘
     │                                              │
     │ 1. Click "Forgot Password"                  │
     │                                              │
     │ 2. POST /api/v1/auth/forgot-password        │
     │    { email: "user@example.com" }            │
     │─────────────────────────────────────────────>│
     │                                              │
     │                                              │ 3. Find user by email
     │                                              │ 4. Generate reset token
     │                                              │ 5. Store token (30 min expiry)
     │                                              │ 6. Send reset email
     │                                              │
     │ 7. Success message                          │
     │<─────────────────────────────────────────────│
     │                                              │
     │ ... user receives email ...                 │
     │                                              │
     │ 8. Click reset link                         │
     │                                              │
     │ 9. GET /reset-password?token=...            │
     │    (Frontend shows password form)           │
     │                                              │
     │ 10. Enter new password                      │
     │                                              │
     │ 11. POST /api/v1/auth/reset-password        │
     │     { token: "...", newPassword: "..." }    │
     │─────────────────────────────────────────────>│
     │                                              │
     │                                              │ 12. Validate token
     │                                              │ 13. Check not expired
     │                                              │ 14. Hash new password
     │                                              │ 15. Update password
     │                                              │ 16. Revoke all refresh tokens
     │                                              │ 17. Mark token as used
     │                                              │
     │ 18. Password reset successfully!            │
     │<─────────────────────────────────────────────│
     │                                              │
     │ 19. Redirect to login                       │
     │                                              │
```

---

## 🔐 Multi-Account Example

**Scenario:** User's journey with multiple auth methods

```
Day 1: Registration with Username/Password
─────────────────────────────────────────────
User creates account:
  ✓ Username: "johndoe"
  ✓ Email: "john@gmail.com"
  ✓ Password: "SecurePass123"

Database:
  Users Table:
    - UserId: "abc-123"
    - Email: "john@gmail.com"
  
  AuthMethods Table:
    - UserId: "abc-123"
    - Provider: "local"
    - ExternalId: "johndoe"
    - PasswordHash: "$2a$12$..."


Day 7: User Links Google Account
─────────────────────────────────
User goes to Settings → Link Google
Authenticates with Google (john@gmail.com)

Database:
  Users Table:
    - UserId: "abc-123"
    - Email: "john@gmail.com"
  
  AuthMethods Table:
    - UserId: "abc-123", Provider: "local", ExternalId: "johndoe"
    - UserId: "abc-123", Provider: "google", ExternalId: "102938475..."


Day 14: User Uses Telegram Mini App
────────────────────────────────────
Opens Telegram bot, links account
Provides email: john@gmail.com

Database:
  Users Table:
    - UserId: "abc-123"
    - Email: "john@gmail.com"
  
  AuthMethods Table:
    - UserId: "abc-123", Provider: "local", ExternalId: "johndoe"
    - UserId: "abc-123", Provider: "google", ExternalId: "102938475..."
    - UserId: "abc-123", Provider: "telegram", ExternalId: "987654321"


Result: User can now log in THREE ways:
  1. Username "johndoe" + password
  2. "Sign in with Google"
  3. Telegram Mini App

All methods access the SAME user account (abc-123)
```

---

## 📝 Key Design Decisions

### Email as Primary Identifier
- Email links accounts across providers
- If OAuth email matches existing account → Prompt to link
- Telegram users can add email to enable linking

### Account Linking Strategy
- Logged-in users can link additional methods
- New OAuth users with existing email → Must log in first, then link
- Prevents accidental account merges

### Security Measures
- Access tokens: 30 minutes (short-lived)
- Refresh tokens: 30 days (one-time use, rotate on refresh)
- Password reset tokens: 30 minutes
- Email verification: No expiration
- Rate limiting on all auth endpoints

### Token Storage
- Access token: Memory (React state/context)
- Refresh token: HttpOnly secure cookie (backend sets)
- Never use localStorage for tokens (XSS vulnerability)

---

**See Full Details:**
- [AUTHENTICATION.md](./AUTHENTICATION.md) - Complete authentication design
- [FRONTEND_AUTH_GUIDE.md](../../aloevera-harmony-meet/docs/FRONTEND_AUTH_GUIDE.md) - Frontend integration guide
