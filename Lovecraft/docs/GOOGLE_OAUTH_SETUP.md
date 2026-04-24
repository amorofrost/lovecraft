# Google OAuth (ID token) setup

This backend uses **Google Identity Services** (frontend obtains an **ID token JWT**) and the API validates that token using the **OAuth 2.0 Web client ID** as the audience.

The backend then issues its own **JWT access/refresh** tokens for the app.

---

## What you need from Google

- An OAuth 2.0 **Web application** client ID that looks like:
  - `1234567890-abc123.apps.googleusercontent.com`

You do **not** need the client secret for the current flow.

See the frontend repo guide for full Google Cloud Console steps:

- `aloevera-harmony-meet/docs/GOOGLE_OAUTH_SETUP.md`

---

## Configure the backend

### Environment variable (recommended)

- `GOOGLE_OAUTH_CLIENT_ID=<your client id>`

### appsettings.json

`Lovecraft.Backend/appsettings.json`:

```json
{
  "Google": {
    "ClientId": "1234567890-abc123.apps.googleusercontent.com"
  }
}
```

---

## Endpoints

- `GET /api/v1/auth/google-config`
  - returns `clientId` for the frontend (not a secret)
- `POST /api/v1/auth/google-login`
  - body: `{ "idToken": "<jwt>" }`
  - returns `signedIn | pending | emailConflict`
- `POST /api/v1/auth/google-register`
  - body: `{ ticket, name, age, location, gender, bio, inviteCode? }`

