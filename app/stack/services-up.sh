#!/usr/bin/env bash
# Starts Postgres, RustFS and Keycloak from compose.yaml and waits until each one answers. Needs
# no legacy data. CICHLIDS_POSTGRES_WAIT_SECONDS (default 60) bounds the wait for Postgres; the
# script fails when Postgres is not ready by then. With --wait-realm it also waits for the
# imported cichlids realm, which Keycloak serves a few seconds after the master realm.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/compose.yaml"
KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8180}"
CICHLIDS_POSTGRES_WAIT_SECONDS="${CICHLIDS_POSTGRES_WAIT_SECONDS:-60}"

wait_realm=false
case "${1:-}" in
  "") ;;
  --wait-realm) wait_realm=true ;;
  *)
    echo "Usage: $0 [--wait-realm]" >&2
    exit 2
    ;;
esac

wait_for_tcp() {
  local host="$1" port="$2" label="$3" tries="${4:-60}"
  for ((i = 0; i < tries; i++)); do
    if (exec 3<>"/dev/tcp/$host/$port") 2>/dev/null; then
      exec 3>&- 3<&-
      return 0
    fi
    sleep 1
  done
  echo "Timed out waiting for $label on $host:$port." >&2
  return 1
}

wait_for_http() {
  local url="$1" label="$2" tries="${3:-120}"
  for ((i = 0; i < tries; i++)); do
    if curl -fsS -o /dev/null "$url" 2>/dev/null; then
      return 0
    fi
    sleep 1
  done
  echo "Timed out waiting for $label at $url." >&2
  return 1
}

docker compose -f "$COMPOSE_FILE" up -d postgres rustfs keycloak

echo "Waiting for Postgres..."
postgres_ready=false
for ((i = 0; i < CICHLIDS_POSTGRES_WAIT_SECONDS; i++)); do
  if docker compose -f "$COMPOSE_FILE" exec -T postgres pg_isready -U cichlids -d cichlids >/dev/null 2>&1; then
    postgres_ready=true
    break
  fi
  sleep 1
done
if [ "$postgres_ready" != true ]; then
  echo "Postgres did not become ready within ${CICHLIDS_POSTGRES_WAIT_SECONDS} seconds." >&2
  exit 1
fi
echo "Postgres is up."

echo "Waiting for RustFS..."
wait_for_tcp 127.0.0.1 9000 "RustFS"
echo "RustFS is up."

echo "Waiting for Keycloak..."
wait_for_http "$KEYCLOAK_URL/realms/master" "Keycloak"
echo "Keycloak is up."

if [ "$wait_realm" = true ]; then
  echo "Waiting for the cichlids realm..."
  wait_for_http "$KEYCLOAK_URL/realms/cichlids" "the cichlids realm"
  echo "The cichlids realm is up."
fi
