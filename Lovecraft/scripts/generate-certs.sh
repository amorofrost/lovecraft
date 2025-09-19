#!/usr/bin/env bash
set -euo pipefail

# Simple OpenSSL-based CA + server + client cert generation for local testing.
# Produces: ca.pem, ca.key, server.crt, server.key, server.pfx, client.crt, client.key, client.pfx
# Usage: ./generate-certs.sh ./output

OUT_DIR=${1:-./certs}
mkdir -p "$OUT_DIR"

# 1) Generate CA
openssl genrsa -out "$OUT_DIR/ca.key" 4096
openssl req -x509 -new -nodes -key "$OUT_DIR/ca.key" -sha256 -days 3650 -subj "/CN=local-ca" -out "$OUT_DIR/ca.crt"

# 2) Server key & CSR
openssl genrsa -out "$OUT_DIR/server.key" 2048
openssl req -new -key "$OUT_DIR/server.key" -subj "/CN=webapi" -out "$OUT_DIR/server.csr"

# 3) Sign server cert with CA
cat > "$OUT_DIR/server_ext.cnf" <<EOF
subjectAltName = DNS:webapi,IP:127.0.0.1
extendedKeyUsage = serverAuth
EOF

openssl x509 -req -in "$OUT_DIR/server.csr" -CA "$OUT_DIR/ca.crt" -CAkey "$OUT_DIR/ca.key" -CAcreateserial -out "$OUT_DIR/server.crt" -days 365 -sha256 -extfile "$OUT_DIR/server_ext.cnf"

# 4) Create server PKCS12 (PFX) for Kestrel if desired (no password here)
openssl pkcs12 -export -out "$OUT_DIR/server.pfx" -inkey "$OUT_DIR/server.key" -in "$OUT_DIR/server.crt" -certfile "$OUT_DIR/ca.crt" -passout pass:

# 5) Client key & CSR
openssl genrsa -out "$OUT_DIR/client.key" 2048
openssl req -new -key "$OUT_DIR/client.key" -subj "/CN=telegrambot-client" -out "$OUT_DIR/client.csr"

cat > "$OUT_DIR/client_ext.cnf" <<EOF
extendedKeyUsage = clientAuth
EOF

# 6) Sign client cert with CA
openssl x509 -req -in "$OUT_DIR/client.csr" -CA "$OUT_DIR/ca.crt" -CAkey "$OUT_DIR/ca.key" -CAcreateserial -out "$OUT_DIR/client.crt" -days 365 -sha256 -extfile "$OUT_DIR/client_ext.cnf"

# 7) Client PFX (optionally protected with a password)
CLIENT_PFX_PASS=${CLIENT_PFX_PASS:-""}
if [ -z "$CLIENT_PFX_PASS" ]; then
  openssl pkcs12 -export -out "$OUT_DIR/client.pfx" -inkey "$OUT_DIR/client.key" -in "$OUT_DIR/client.crt" -certfile "$OUT_DIR/ca.crt" -passout pass:
else
  openssl pkcs12 -export -out "$OUT_DIR/client.pfx" -inkey "$OUT_DIR/client.key" -in "$OUT_DIR/client.crt" -certfile "$OUT_DIR/ca.crt" -passout pass:$CLIENT_PFX_PASS
fi

# 8) Blazor server key & CSR
openssl genrsa -out "$OUT_DIR/blazor_server.key" 2048
openssl req -new -key "$OUT_DIR/blazor_server.key" -subj "/CN=blazor" -out "$OUT_DIR/blazor_server.csr"

cat > "$OUT_DIR/blazor_server_ext.cnf" <<EOF
subjectAltName = DNS:blazor,IP:127.0.0.1
extendedKeyUsage = serverAuth
EOF

openssl x509 -req -in "$OUT_DIR/blazor_server.csr" -CA "$OUT_DIR/ca.crt" -CAkey "$OUT_DIR/ca.key" -CAcreateserial -out "$OUT_DIR/blazor_server.crt" -days 365 -sha256 -extfile "$OUT_DIR/blazor_server_ext.cnf"

# Blazor server PFX
openssl pkcs12 -export -out "$OUT_DIR/blazor_server.pfx" -inkey "$OUT_DIR/blazor_server.key" -in "$OUT_DIR/blazor_server.crt" -certfile "$OUT_DIR/ca.crt" -passout pass:

# 9) Blazor client key & CSR
openssl genrsa -out "$OUT_DIR/blazor_client.key" 2048
openssl req -new -key "$OUT_DIR/blazor_client.key" -subj "/CN=blazor-client" -out "$OUT_DIR/blazor_client.csr"

cat > "$OUT_DIR/blazor_client_ext.cnf" <<EOF
extendedKeyUsage = clientAuth
EOF

openssl x509 -req -in "$OUT_DIR/blazor_client.csr" -CA "$OUT_DIR/ca.crt" -CAkey "$OUT_DIR/ca.key" -CAcreateserial -out "$OUT_DIR/blazor_client.crt" -days 365 -sha256 -extfile "$OUT_DIR/blazor_client_ext.cnf"

# Blazor client PFX (no password)
if [ -z "$CLIENT_PFX_PASS" ]; then
  openssl pkcs12 -export -out "$OUT_DIR/blazor_client.pfx" -inkey "$OUT_DIR/blazor_client.key" -in "$OUT_DIR/blazor_client.crt" -certfile "$OUT_DIR/ca.crt" -passout pass:
else
  openssl pkcs12 -export -out "$OUT_DIR/blazor_client.pfx" -inkey "$OUT_DIR/blazor_client.key" -in "$OUT_DIR/blazor_client.crt" -certfile "$OUT_DIR/ca.crt" -passout pass:$CLIENT_PFX_PASS
fi

ls -la "$OUT_DIR"

echo "Generated certs in $OUT_DIR"

echo "To use: mount server.pfx into the WebAPI container and set environment variables or configure Kestrel to load it. Mount client.pfx into the client container and set CLIENT_CERT_PATH to its path. Also mount ca.crt for server-side chain validation (Certificates:CaPath)."

# Extract SHA-1 thumbprints (no-colon, upper-case) for easy pinning
extract_thumbprint() {
  local certfile="$1"
  if [ -f "$certfile" ]; then
    # output like: SHA1 Fingerprint=AA:BB:CC:...
    local fp_colon
    fp_colon=$(openssl x509 -in "$certfile" -noout -fingerprint -sha1 2>/dev/null | cut -d'=' -f2)
    if [ -n "$fp_colon" ]; then
      # remove colons and uppercase
      local fp_clean
      fp_clean=$(echo "$fp_colon" | tr -d ':' | tr '[:lower:]' '[:upper:]')
      echo "$fp_clean"
      return 0
    fi
  fi
  return 1
}

CA_FP=$(extract_thumbprint "$OUT_DIR/ca.crt" || true)
SERVER_FP=$(extract_thumbprint "$OUT_DIR/server.crt" || true)
CLIENT_FP=$(extract_thumbprint "$OUT_DIR/client.crt" || true)
BLZ_SERVER_FP=$(extract_thumbprint "$OUT_DIR/blazor_server.crt" || true)
BLZ_CLIENT_FP=$(extract_thumbprint "$OUT_DIR/blazor_client.crt" || true)

if [ -n "$CA_FP" ]; then
  echo "CA SHA1 Thumbprint: $CA_FP"
  echo "$CA_FP" > "$OUT_DIR/ca.thumbprint"
fi
if [ -n "$SERVER_FP" ]; then
  echo "Server SHA1 Thumbprint: $SERVER_FP"
  echo "$SERVER_FP" > "$OUT_DIR/server.thumbprint"
fi
if [ -n "$CLIENT_FP" ]; then
  echo "Client SHA1 Thumbprint: $CLIENT_FP"
  echo "$CLIENT_FP" > "$OUT_DIR/client.thumbprint"
fi
if [ -n "$BLZ_SERVER_FP" ]; then
  echo "Blazor Server SHA1 Thumbprint: $BLZ_SERVER_FP"
  echo "$BLZ_SERVER_FP" > "$OUT_DIR/blazor_server.thumbprint"
fi
if [ -n "$BLZ_CLIENT_FP" ]; then
  echo "Blazor Client SHA1 Thumbprint: $BLZ_CLIENT_FP"
  echo "$BLZ_CLIENT_FP" > "$OUT_DIR/blazor_client.thumbprint"
fi
