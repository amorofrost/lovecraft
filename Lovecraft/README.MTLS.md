 # Mutual TLS (mTLS) between TelegramBot and WebAPI

 This file walks through a reproducible local flow that:
 - generates a CA + server + client certificates,
 - builds Docker images for the WebAPI and the Telegram bot,
 - runs both containers on a user-created Docker network with certificates mounted and environment variables set,
 - demonstrates how to pin server and client certificates using thumbprints so only the bot can call the WebAPI.

 Prerequisites
 - Docker installed and the current user allowed to run docker commands.
 - `openssl` available (script uses it).
 - A valid Telegram bot token (create one via BotFather). We'll expose it to the bot container via an environment variable.

 Quick reproducible steps

 1) Generate certs

 ```bash
 # from repo root
 ./scripts/generate-certs.sh ./certs
 ls -la ./certs
 # The script will also print and write thumbprints to ./certs/*.thumbprint
 cat ./certs/server.thumbprint
 cat ./certs/client.thumbprint
 cat ./certs/ca.thumbprint
 ```

 2) Build Docker images

 ```bash
 # create a network for the containers so they can reach each other by name
 docker network create lovecraft-net || true

 # Build WebAPI image
 docker build -f Lovecraft.WebAPI/Dockerfile -t lovecraft-webapi:local .

 # Build TelegramBot image
 docker build -f Lovecraft.TelegramBot/Dockerfile -t lovecraft-telegrambot:local .
 ```

 3) Run the WebAPI container

 - Mount the generated certs into the container (read-only).
 - Configure Kestrel to load `server.pfx` and provide the CA bundle and allowed client thumbprint(s).

 ```bash
 # set absolute path to certs dir for Docker
 CERTS_DIR=$(pwd)/certs

 # read the client thumbprint that the WebAPI should accept (from generated files)
 CLIENT_TP=$(cat "$CERTS_DIR/client.thumbprint")

 docker run -d \
   --name lovecraft-webapi \
   --network lovecraft-net \
   -p 5001:5001 \
   -v "$CERTS_DIR":/app/certs:ro \
   -e ASPNETCORE_Kestrel__Certificates__Default__Path=/app/certs/server.pfx \
   -e ASPNETCORE_Kestrel__Certificates__Default__Password= \
   -e Certificates__CaPath=/app/certs/ca.crt \
   -e Certificates__AllowedClientThumbprints="$CLIENT_TP" \
   lovecraft-webapi:local
```

 4) Run the TelegramBot container (the client)

 - Mount the client cert so the bot can present it during mTLS.
 - Pin the server certificate on the client side using the generated server thumbprint.

 ```bash
 CERTS_DIR=$(pwd)/certs
 SERVER_TP=$(cat "$CERTS_DIR/server.thumbprint")

 # Export your Telegram bot token in the shell first, e.g.:
 # export TELEGRAM_BOT_TOKEN="123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11"

 docker run -d \
   --name lovecraft-telegrambot \
   --network lovecraft-net \
   -v "$CERTS_DIR":/app/certs:ro \
   -e CLIENT_CERT_PATH=/app/certs/client.pfx \
   -e CLIENT_CERT_PASSWORD= \
   -e WebApi__BaseUrl=https://lovecraft-webapi:5001/ \
  -e ALLOWED_SERVER_THUMBPRINTS="$SERVER_TP" \
  -e TELEGRAM_BOT_TOKEN="$TELEGRAM_BOT_TOKEN" \
  -e ACCESS_CODE="$ACCESS_CODE" \
  lovecraft-telegrambot:local
```

 Notes on names/urls
 - The bot uses `WebApi__BaseUrl` to call the API. When both containers are on the same Docker network, use the container name `lovecraft-webapi` as the hostname (the example above uses `https://lovecraft-webapi:5001/`).

 5) Test with your Telegram client

 - Open your Telegram app and send `/start` to your bot (the bot username is printed in logs when it starts).
 - The bot should reply with a greeting and the `WeatherForecast` response fetched from the WebAPI via mTLS.

 Helpful commands while debugging
 ```bash
 # View bot logs
 docker logs -f lovecraft-telegrambot

 # View webapi logs
 docker logs -f lovecraft-webapi

 # Stop and remove containers
 docker rm -f lovecraft-telegrambot lovecraft-webapi || true

 # Remove network
 docker network rm lovecraft-net || true
 ```

 Troubleshooting
 - If the bot fails to call the WebAPI, check `docker logs lovecraft-telegrambot` for certificate validation errors.
 - Confirm thumbprints in `./certs/*.thumbprint` match the environment variables passed to the containers.
 - If you used self-signed certs and didn't set pinning, ensure the bot currently allows the server cert (we default to pinning when configured).

 Security notes
 - This setup is for local testing and demonstration. For production:
   - Use a proper CA or a managed certificate solution (Vault, ACME, cloud PKI).
   - Automate certificate rotation and update pinned thumbprints via a secure secret store.
   - Remove permissive validation and enforce full chain validation + revocation checks.

Git / secrets guidance
- The `certs/` and `certs_test/` directories are now listed in `.gitignore` to avoid committing private keys and certs.
- If you accidentally committed certs earlier, remove them from the git index with:

```bash
# remove files from index but keep locally
git rm --cached -r certs certs_test || true
git commit -m "Remove generated certs from repository"
```


 That's it — after running the above, send `/start` to your Telegram bot and you should receive the WebAPI response over mutual TLS.

Using Docker Compose
--------------------

To simplify local runs you can use `docker-compose.yml` included in the repo root. It builds both services, creates a bridged network, mounts `./certs`, and reads thumbprints and the Telegram token from environment variables.

1) Create a `.env` file in the repo root with these values (example):

```ini
# .env example - replace values with your generated thumbprints and token
TELEGRAM_BOT_TOKEN=123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11
ALLOWED_SERVER_THUMBPRINTS=4751C07F3D796323AAB7D3B2F0F40A25CCB7C714
ALLOWED_CLIENT_THUMBPRINTS=74D00CB9785B2BCC02E88F1466740AE9489F8688
# ACCESS_CODE is the shared secret users must provide to the bot as `/start <ACCESS_CODE>`
ACCESS_CODE=ABC123
```

2) Run compose (build + start):

```bash
# generate certs if you haven't already
./scripts/generate-certs.sh ./certs

# bring the stack up
docker compose up --build -d

# view logs
docker compose logs -f telegrambot
docker compose logs -f webapi
```

3) Tear down

```bash
docker compose down
```

Notes
- `docker compose` (v2) is used above; if you have the old `docker-compose` binary, replace the command accordingly.
- Ensure `./certs` contains the files produced by `scripts/generate-certs.sh` and that your `.env` thumbprints match the generated files.

Next steps
- If you'll run this more than occasionally, I can add a small admin script to extract and populate the `.env` automatically from `./certs` and to rotate thumbprints.

Auto-generate `.env` and Makefile
---------------------------------

To simplify creating the `.env` with the thumbprints we added `scripts/generate-env.sh`. It will read `./certs/*.thumbprint` and produce a `.env` with `ALLOWED_SERVER_THUMBPRINTS` and `ALLOWED_CLIENT_THUMBPRINTS`. You still need to fill `TELEGRAM_BOT_TOKEN`.

```bash
# after generating certs
./scripts/generate-env.sh ./certs .env
# edit .env, fill TELEGRAM_BOT_TOKEN
```

There is also a `Makefile` with common shortcuts:

```bash
make certs    # generate certs into ./certs
make env      # generate .env from ./certs
make build    # docker compose build
make up       # docker compose up --build -d
make logs     # docker compose logs -f
make down     # docker compose down
make clean    # remove containers and network
```

With these tools you can go from zero to running in two commands:

```bash
make certs
./scripts/generate-env.sh ./certs .env   # or make env
# edit .env and add TELEGRAM_BOT_TOKEN
make up
```
