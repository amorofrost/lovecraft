Lovecraft Telegram Bot
======================

Overview
--------
This service implements a Telegram bot that integrates with the Lovecraft Web API. It provides a small set of commands, a user registration flow, profile viewing and a simple like callback. The bot is implemented in `BotMessageHandler`, sending messages and photos through `BotSender`, and started by `BotHostedService`.

High-level flow
---------------
- `BotHostedService` starts the `ITelegramBotClient` and routes incoming updates to `BotMessageHandler`.
- `BotMessageHandler` handles text messages, photo uploads (used during registration), and callback queries (e.g., "Like" button).
- `BotSender` wraps `ITelegramBotClient` and provides helpers like `SendProfileCardAsync` which sends a user's avatar (by Telegram file_id or by URL) and an inline "Like" button.
- Registration info is held in-memory per Telegram user id in a `ConcurrentDictionary` while the user is progressing through the steps.

Supported commands
------------------
- `/start <access_code>`
  - Validates the provided access code with `IAccessCodeManager` (the default implementation reads allowed codes from the `ACCESS_CODE` environment variable).
  - If the Telegram user already exists in the Lovecraft Web API, the bot greets the user and shows service health.
  - If the user is not found, registration is started and the bot will ask a series of questions (see "Registration flow").

- `/help`
  - Shows the available commands and short usage.

- `/me`
  - Displays the profile card for the user calling the command (requires the user to exist in the Web API).

- `/next`
  - Requests the next profile from the Web API and sends it as a profile card (same format as `/me`).

Other interactions
------------------
- Photo messages
  - Used during registration. When the bot expects a photo (stage `WaitingPhoto`), the user should send a photo in the chat. The bot selects the largest photo size and uses the returned Telegram `file_id` as the user's avatar.

- Callback queries
  - Profile cards include a small inline button `👍 Like` with callback data in the form `like:<userId>`. The handler replies to the callback query with a short acknowledgement.

Registration flow (detailed)
----------------------------
The registration is an interactive multi-step flow stored in memory until it completes:
1. WaitingName — the bot asks: "Please enter your name". The name is validated for non-empty and maximal length.
2. WaitingUsername — the bot asks for a desired login username. The handler checks availability by calling `ILovecraftApiClient.IsUsernameAvailableAsync(normalizedUsername)`. If taken, the user is asked to pick another.
3. WaitingPassword — the bot asks for a password (basic length checks; server will validate more strictly).
4. WaitingPhoto — the bot asks the user to send a photo to use as avatar. After receiving the photo it calls `ILovecraftApiClient.CreateUserAsync(CreateUserRequest)`.
   - If the API returns a 409 Conflict (detected in an HttpRequestException), the bot rolls the user back to the `WaitingUsername` stage and asks to pick another username.
   - On success, the bot confirms account creation and may show the API health information.

Notes about persistence: registration state is stored in-memory (`ConcurrentDictionary<long, RegistrationState>`). If the bot restarts mid-registration the in-progress state will be lost.

Configuration
-------------
The bot reads configuration from `appsettings.json` and environment variables. Key configuration values:

- Telegram token:
  - `appsettings.json` key: `Telegram:BotToken`
  - environment variable fallback: `TELEGRAM_BOT_TOKEN`

- Access codes (valid tokens allowed with `/start <access_code>`):
  - Default manager reads from environment variable `ACCESS_CODE` (comma-separated list). The implementation is `EnvironmentVariableAccessCodeManager`.

- Web API client / certificate options (used when calling Lovecraft Web API):
  - `WebApi:BaseUrl` — base URL of the Lovecraft Web API (default `https://webapi:5001/` if not provided).
  - `Certificates:ClientCertPath` or environment variable `CLIENT_CERT_PATH` — path to a client certificate file used for mTLS to the Web API.
  - `Certificates:ClientCertPassword` or environment variable `CLIENT_CERT_PASSWORD` — optional password for the client certificate.
  - `WebApi:AllowedServerThumbprints` or environment variable `ALLOWED_SERVER_THUMBPRINTS` — comma-separated server certificate thumbprints used for simple pinning. If unset, the HttpClient will accept any server certificate (the code currently uses the "dangerous accept any" validator if no thumbprints are configured).

Sample environment variables
----------------------------
To run the bot locally you can set environment variables before starting the app (example for bash):

```bash
export TELEGRAM_BOT_TOKEN="123456:ABCDEF..."
# Optional: mTLS client certificate for the Lovecraft Web API
export CLIENT_CERT_PATH="/path/to/client.pfx"
export CLIENT_CERT_PASSWORD="pfx-password"
# Optional: allowed server thumbprints if you want server pinning
export ALLOWED_SERVER_THUMBPRINTS="THUMBPRINT1,THUMBPRINT2"
# Access codes that allow users to /start (comma-separated)
export ACCESS_CODE="invitecode1,invitecode2"
# Optional: WebAPI base url
export LOVESRAFT_WEBAPI_BASEURL="https://localhost:5001/"
```

If you use `appsettings.json`, put the keys under respective sections (example):

```json
{
  "Telegram": { "BotToken": "123456:ABCDEF" },
  "WebApi": { "BaseUrl": "https://localhost:5001/", "AllowedServerThumbprints": "" },
  "Certificates": { "ClientCertPath": "/certs/client.pfx", "ClientCertPassword": "password" }
}
```

Running
-------
From the `Lovecraft.TelegramBot` project folder you can run with the .NET SDK:

```bash
cd Lovecraft.TelegramBot
dotnet run
```

A `Dockerfile` is included for containerized runs; if you prefer docker-compose, follow the repository-level docker-compose setup.

Important implementation details and edge cases
---------------------------------------------
- Access control: the bot requires a valid access code via `/start <access_code>` before allowing registration. The set of valid codes is provided by `ACCESS_CODE` env var by default.
- Username availability is checked via the Web API before advancing the registration.
- Photo handling: the bot expects a photo message during `WaitingPhoto`. If the user sends other content while in that stage, the bot will prompt appropriately.
- Concurrency: registration state is stored per Telegram user id in memory; there is no persistent storage for in-progress registrations.
- Error handling: API errors during creation are logged. On 409 Conflict the bot returns the user to the username step. Other HTTP failures keep the user at `WaitingPhoto` so they may retry the upload.
- Server certificate pinning: if `WebApi:AllowedServerThumbprints` (or `ALLOWED_SERVER_THUMBPRINTS`) is empty, the code currently disables certificate validation (accepts any server cert). This is convenient for local/dev setups but not safe for production — configure allowed thumbprints in production.

Troubleshooting
---------------
- "Bot token missing." — Ensure `TELEGRAM_BOT_TOKEN` is set or `Telegram:BotToken` is present in `appsettings.json`.
- TLS / certificate errors — confirm `CLIENT_CERT_PATH` and optional password are correct and that `ALLOWED_SERVER_THUMBPRINTS` contains the server certificate thumbprint if pinning is required.
- Registration fails with duplicate username — the bot will ask you to pick another username when the Web API reports the username is taken.

Files of interest
-----------------
- `BotMessageHandler.cs` — message handling logic and registration flow.
- `BotSender.cs` — Telegram sending helpers and profile card rendering.
- `BotHostedService.cs` — background service that receives updates and dispatches them to the handler.
- `Program.cs` — DI wiring and HttpClient + certificate configuration.

Contact / Next steps
--------------------
If you want the bot to persist in-progress registrations across restarts, consider moving `RegistrationState` into a persistent store (database or Redis) and restore/resume on startup. For production use, configure server certificate pinning and use mTLS correctly.
