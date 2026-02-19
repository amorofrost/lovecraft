# Authentication Simplification

## Changes Made

### Email as Login

**Before:**
- Separate `username` and `email` fields
- Users could log in with either username or email
- Both had to be unique

**After:**
- Email is the only login identifier
- Simpler registration form
- Cleaner data model

---

## Updated Data Contracts

### RegisterRequestDto
```csharp
public class RegisterRequestDto
{
    public string Email { get; set; } = string.Empty;      // Login identifier
    public string Password { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;        // Display name
    public int Age { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
}
```

### UserInfo
```csharp
public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;      // Login identifier
    public string Name { get; set; } = string.Empty;        // Display name
    public bool EmailVerified { get; set; }
    public List<string> AuthMethods { get; set; } = new();
}
```

### JWT Claims
```csharp
// JWT token now contains:
- ClaimTypes.NameIdentifier: userId (GUID)
- ClaimTypes.Email: email (login identifier)
- ClaimTypes.Name: name (display name, not username)
```

---

## Frontend Changes

### Registration Form (Welcome.tsx)

**Removed:**
- Username field

**Updated:**
- Email field now has helper text: "Your email will be used as your login"
- Display name field remains for user's preferred name

**Form Structure:**
```typescript
const [registerData, setRegisterData] = useState({
  email: '',        // Login identifier
  password: '',
  name: '',         // Display name
  bio: '',
  location: '',
  age: '',
  gender: ''
});
```

### Login Form

Unchanged - still uses email and password

---

## Backend Changes

### MockUser Model
```csharp
private class MockUser
{
    public string Id { get; set; }
    public string Email { get; set; }          // Login identifier (indexed)
    public string Name { get; set; }           // Display name
    public string PasswordHash { get; set; }
    public bool EmailVerified { get; set; }
    public List<string> AuthMethods { get; set; }
    // ... other fields
}
```

### Storage Keys

**Before:**
```csharp
_users[user.Email.ToLower()] = user;
_users[user.Username.ToLower()] = user;  // Duplicate storage
```

**After:**
```csharp
_users[user.Email.ToLower()] = user;  // Single key
```

---

## API Examples

### Register
```bash
curl -X POST http://localhost:5000/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "user@example.com",
    "password": "MyPass123!@#",
    "name": "John Doe",
    "age": 25,
    "location": "City, Country",
    "gender": "Male",
    "bio": "About me"
  }'
```

### Login
```bash
curl -X POST http://localhost:5000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -c cookies.txt \
  -d '{
    "email": "user@example.com",
    "password": "MyPass123!@#"
  }'
```

### Response
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOi...",
    "refreshToken": "eyJhbGciOi...",
    "user": {
      "id": "guid-here",
      "email": "user@example.com",
      "name": "John Doe",
      "emailVerified": false,
      "authMethods": ["local"]
    },
    "expiresAt": "2024-12-01T10:15:00Z"
  }
}
```

---

## Benefits

1. **Simpler User Experience**
   - One less field to remember
   - Email is familiar as a login identifier
   - Reduces confusion

2. **Cleaner Data Model**
   - No username/email duplication
   - Single unique identifier (email)
   - Simpler validation

3. **Easier Implementation**
   - No username uniqueness checks
   - Single storage key
   - Reduced complexity

4. **OAuth Compatibility**
   - OAuth providers return email
   - Natural account linking
   - Consistent across auth methods

---

## Migration Notes

**For Azure Storage Implementation:**
- PartitionKey: Email domain (e.g., "example.com")
- RowKey: Email local part + hash (e.g., "user-abc123")
- Email stored as indexed field
- No username field needed

**For Future Features:**
- Display names can be changed by users
- Email remains fixed (or requires verification to change)
- Usernames could be added later as optional @handles (if needed)

---

## Test Data

**Test User:**
```
Email: test@example.com
Password: Test123!@#
Name: Test User
```

All 16 unit tests passing ✅
