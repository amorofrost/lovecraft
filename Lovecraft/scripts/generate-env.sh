#!/usr/bin/env bash
set -euo pipefail

# Usage: ./scripts/generate-env.sh ./certs .env
CERT_DIR=${1:-./certs}
OUT_FILE=${2:-.env}

if [ ! -d "$CERT_DIR" ]; then
  echo "Cert directory $CERT_DIR does not exist"
  exit 1
fi

read_file() {
  local f=$1
  if [ -f "$f" ]; then
    cat "$f"
  else
    echo "";
  fi
}

SERVER_TP=$(read_file "$CERT_DIR/server.thumbprint" | tr -d '\n')
CLIENT_TP=$(read_file "$CERT_DIR/client.thumbprint" | tr -d '\n')
CA_TP=$(read_file "$CERT_DIR/ca.thumbprint" | tr -d '\n')
BLZ_CLIENT_TP=$(read_file "$CERT_DIR/blazor_client.thumbprint" | tr -d '\n')
BLZ_SERVER_TP=$(read_file "$CERT_DIR/blazor_server.thumbprint" | tr -d '\n')

if [ -z "$SERVER_TP" ] || [ -z "$CLIENT_TP" ]; then
  echo "Missing server or client thumbprint in $CERT_DIR. Run ./scripts/generate-certs.sh first."
  exit 2
fi

cat > "$OUT_FILE" <<EOF
# Auto-generated .env by scripts/generate-env.sh
TELEGRAM_BOT_TOKEN=
ALLOWED_SERVER_THUMBPRINTS=$SERVER_TP
# Allowed client thumbprints: telegram bot client and blazor client (comma-separated)
ALLOWED_CLIENT_THUMBPRINTS=$CLIENT_TP${BLZ_CLIENT_TP:+,}$BLZ_CLIENT_TP
# Blazor server certificate thumbprint (for informational purposes)
BLZ_SERVER_THUMBPRINT=$BLZ_SERVER_TP
# Access code for regestering new accounts (change this!)
ACCESS_CODE=ABC123
EOF

echo "Wrote $OUT_FILE with server and client thumbprints. Please fill TELEGRAM_BOT_TOKEN."