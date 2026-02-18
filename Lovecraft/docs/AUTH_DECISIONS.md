# Authentication Design - Summary of Decisions

## ✅ Finalized Design Decisions

Based on requirements clarification, here are the confirmed design decisions for the AloeVera Harmony Meet authentication system:

---

### 1. Primary Identifier

**Decision:** UserId (GUID) is the primary identifier

- Every user has a unique GUID as their primary identifier
- Email is unique when present, but nullable (Telegram-only users)
- Username is optional and nullable

**Rationale:** Allows users without email (Telegram-only) while maintaining account uniqueness

---

### 2. Email Uniqueness & Verification

**Decision:** Email must be unique when present, verification required for username/password

- If a user has an email, it must be unique in the system
- Username/password registration: Email verification **required before system access**
- OAuth accounts: Email pre-verified (provider already verified)
- Telegram-only users: No email required (can add later)

**Rationale:** Prevents duplicate accounts while supporting email-less Telegram users

---

### 3. Smart Account Linking

**Decision:** Automatically link OAuth accounts when email matches

**Flow:**
```
User A registers with email/password: alice@example.com
Later, User A tries "Sign in with Google" using same email
→ System automatically links Google to existing account
→ No duplicate account created
→ User sees: "Your Google account has been linked"
```

**Benefits:**
- Seamless UX - no confusion about duplicate accounts
- Prevents accidental duplicates
- User can use any linked method to log in

**Security:** Email from verified OAuth provider is trusted for automatic linking

---

### 4. Authentication Method Equality

**Decision:** All authentication methods are equal - no "primary" method

- Username/Password, Google, Facebook, VK, Telegram are all equal
- Users can log in with any linked method
- No hierarchy or preferred method

**Rationale:** Flexibility - users choose their preferred login method

---

### 5. Password Requirements

**Decision:** Standard strong password requirements

- Minimum 8 characters
- At least one uppercase letter (A-Z)
- At least one lowercase letter (a-z)
- At least one number (0-9)
- At least one special character (!@#$%^&*()_+-=[]{}|;:,.<>?)

**Rationale:** Industry-standard security without being overly restrictive

---

### 6. Telegram Users Without Email

**Decision:** Telegram users can use the system without providing email

- Telegram-only authentication doesn't require email
- Users can add email later if they want to:
  - Add username/password authentication
  - Link OAuth providers
- If user links OAuth, email from OAuth becomes their email

**Rationale:** Lower barrier to entry for Telegram users

---

### 7. Token Lifetimes

**Decision:** Balanced token lifetimes for security and UX

- **Access Token:** 15 minutes
  - Short-lived for security
  - Automatic refresh in background
- **Refresh Token:** 7 days
  - Reasonable session length
  - One-time use with rotation
  - Can be revoked

**Rationale:** Security best practices - short access tokens, reasonable refresh window

---

### 8. Account Unlinking Rules

**Decision:** Can unlink auth methods, but must keep at least one

- Users can remove authentication methods
- System prevents unlinking the last method
- Requires password confirmation (if password exists)

**Example:**
```
User has: Email/Password + Google + Telegram
Can unlink: Google ✓
Can unlink: Telegram ✓
Can unlink: Email/Password ✓
Cannot unlink all three at once ✗
```

**Rationale:** Prevents users from accidentally locking themselves out

---

## 🔄 User Journey Examples

### Example 1: Standard Registration Flow

```
Day 1: User registers with email/password
  → Email: john@example.com
  → Must verify email before accessing system
  → Receives verification email
  → Clicks link, email verified
  → Can now use the system

Day 7: User adds Google
  → Goes to Settings → "Link Google Account"
  → Authenticates with Google
  → Google added to account
  → Can now log in with email/password OR Google
```

### Example 2: OAuth First, Then Password

```
Day 1: User signs in with Google
  → Email: alice@gmail.com (auto-verified via Google)
  → Account created
  → Can access system immediately

Day 5: User adds password
  → Goes to Settings → "Add Password"
  → Provides username + password
  → Password added
  → Can now log in with Google OR username/password
```

### Example 3: Smart Linking Prevents Duplicate

```
Day 1: User registers with email/password
  → Email: bob@example.com
  → Verifies email

Day 3: User forgets they have account
  → Tries "Sign in with Google" (bob@example.com)
  → System detects email exists
  → Automatically links Google to existing account
  → Shows: "Your Google account has been linked"
  → No duplicate account created
```

### Example 4: Telegram-Only User

```
Day 1: User opens Telegram Mini App
  → Authenticates via Telegram
  → Account created (no email)
  → Can use system immediately

Day 10: User wants to add password
  → Must provide email first
  → Provides: maria@example.com
  → Verifies email
  → Can now add password
  → Can log in via Telegram OR email/password
```

---

## 🔐 Security Summary

### Password Security
- BCrypt hashing (cost factor 12)
- Random 128-bit salt per password
- Password requirements enforced
- Optional: Prevent reuse of last 5 passwords

### Token Security
- Access Token: 15 minutes, HMAC-SHA256 signed
- Refresh Token: 7 days, one-time use, stored hashed
- Tokens revoked on logout/password change
- Secret keys in Azure Key Vault

### OAuth Security
- State parameter for CSRF protection
- PKCE for public clients
- Minimum scope requests
- Server-side token validation

### Telegram Security
- InitData HMAC signature validation
- Timestamp check (5 minute window)
- Bot token never exposed to client

### Rate Limiting
- Login: 5 attempts per 15 minutes per IP
- Registration: 3 per hour per IP
- Password reset: 3 per hour per email
- Token refresh: 10 per minute per user

### Account Protection
- Account lockout after 5 failed logins (15 minute lockout)
- Email verification required (username/password)
- Session tracking and revocation
- Security notifications (future)

---

## 📊 Database Schema Summary

### Users Table
```
PartitionKey: "USER"
RowKey: {UserId}
- UserId (GUID) - PRIMARY IDENTIFIER
- Email (string, nullable, unique when set)
- EmailVerified (bool)
- Username (string, nullable)
- CreatedAt, UpdatedAt, LastLoginAt
- IsActive (bool)
```

### AuthMethods Table
```
PartitionKey: {UserId}
RowKey: {Provider}_{ExternalId}
- UserId (GUID)
- Provider ("local", "google", "facebook", "vk", "telegram")
- ExternalId (provider's user ID, or username for local)
- PasswordHash (nullable)
- PasswordSalt (nullable)
- LinkedAt, LastUsedAt
```

### RefreshTokens Table
```
PartitionKey: {UserId}
RowKey: {TokenId}
- TokenHash
- ExpiresAt (7 days from creation)
- CreatedAt, RevokedAt
- ReplacedByTokenId (for rotation)
```

---

## 🚀 Implementation Priority

### Phase 1: Core Authentication (2-3 weeks)
- [x] Design completed
- [ ] Username/password registration with email verification
- [ ] Login/logout
- [ ] JWT token generation and validation
- [ ] Token refresh mechanism
- [ ] Password reset flow

### Phase 2: OAuth Integration (2 weeks)
- [ ] Google OAuth
- [ ] Facebook OAuth
- [ ] VK OAuth
- [ ] Smart account linking

### Phase 3: Telegram Integration (1 week)
- [ ] Telegram initData validation
- [ ] Telegram login
- [ ] Account linking for Telegram users

### Phase 4: Polish & Security (1 week)
- [ ] Rate limiting
- [ ] Account lockout
- [ ] Security notifications
- [ ] Session management UI
- [ ] Testing and hardening

---

## 📖 Documentation References

- **[AUTHENTICATION.md](./AUTHENTICATION.md)** - Complete technical specification (26KB)
- **[AUTH_FLOWS.md](./AUTH_FLOWS.md)** - Visual flow diagrams (15KB)
- **[FRONTEND_AUTH_GUIDE.md](../../aloevera-harmony-meet/docs/FRONTEND_AUTH_GUIDE.md)** - Frontend integration guide (17KB)

---

**Version:** 1.1  
**Status:** ✅ Finalized - Ready for Implementation  
**Last Updated:** 2026-02-18
