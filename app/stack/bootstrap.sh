#!/usr/bin/env bash
# Brings a clean checkout up to the full local MVP stand: Docker services, legacy MySQL
# reachability, ETL migration and seed, the Keycloak realm import, and the legacy account import
# into Keycloak. Every step is safe to repeat: the service, legacy MySQL and realm steps skip
# work that is done, and the ETL, media and account import steps rerun idempotently. A run
# against an already-bootstrapped stack ends by printing the next steps (starting the API and
# the app).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
API_DIR="$REPO_ROOT/app/api"
COMPOSE_FILE="$SCRIPT_DIR/compose.yaml"

CICHLIDS_LEGACY_STACK_DIR="${CICHLIDS_LEGACY_STACK_DIR:-/workspaces/cichlids.com/workspace-legacy/legacy-stack}"
CICHLIDS_MEDIA_LIMIT="${CICHLIDS_MEDIA_LIMIT:-3000}"
CICHLIDS_AUTH0_EXPORT="${CICHLIDS_AUTH0_EXPORT:-/workspaces/legacy-data/auth0/auth0-cichlids.json}"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"

step() { printf '\n== %s ==\n' "$1"; }

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
  local url="$1" label="$2" tries="${3:-90}"
  for ((i = 0; i < tries; i++)); do
    if curl -fsS -o /dev/null "$url" 2>/dev/null; then
      return 0
    fi
    sleep 1
  done
  echo "Timed out waiting for $label at $url." >&2
  return 1
}

etl() {
  (cd "$API_DIR" && "$DOTNET" run --project src/Cichlids.Etl -c Release -- "$@")
}

# 1. Postgres, RustFS, Keycloak
step "Docker services (postgres, rustfs, keycloak)"
docker compose -f "$COMPOSE_FILE" up -d postgres rustfs keycloak

echo "Waiting for Postgres..."
for ((i = 0; i < 60; i++)); do
  if docker compose -f "$COMPOSE_FILE" exec -T postgres pg_isready -U cichlids -d cichlids >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
echo "Postgres is up."

echo "Waiting for RustFS..."
wait_for_tcp 127.0.0.1 9000 "RustFS"
echo "RustFS is up."

echo "Waiting for Keycloak..."
wait_for_http "http://localhost:8180/realms/master" "Keycloak"
echo "Keycloak is up."

# 2. Legacy MySQL stack
step "Legacy MySQL (port 3306)"
if wait_for_tcp 127.0.0.1 3306 "legacy MySQL" 1 2>/dev/null; then
  echo "Legacy MySQL is already reachable, skipping."
elif [ -f "$CICHLIDS_LEGACY_STACK_DIR/docker-compose.yml" ]; then
  echo "Starting the legacy MySQL stack from $CICHLIDS_LEGACY_STACK_DIR..."
  docker compose -f "$CICHLIDS_LEGACY_STACK_DIR/docker-compose.yml" up -d
  wait_for_tcp 127.0.0.1 3306 "legacy MySQL"
  echo "Legacy MySQL is up."
else
  cat >&2 <<EOF
Legacy MySQL is not reachable on 127.0.0.1:3306 and no compose file was found at
  $CICHLIDS_LEGACY_STACK_DIR/docker-compose.yml
Start it manually (cd workspace-legacy/legacy-stack && docker compose up -d) or set
CICHLIDS_LEGACY_STACK_DIR to point at that directory, then re-run this script.
EOF
  exit 1
fi

# 3. ETL: migrate, all steps, verify
step "ETL migrate"
etl migrate

step "ETL all steps (includes an internal verify pass)"
etl all

step "ETL verify"
etl verify

# 4. Media
step "Media seed (limit=${CICHLIDS_MEDIA_LIMIT})"
if [ "$CICHLIDS_MEDIA_LIMIT" = "0" ]; then
  etl media
else
  etl media --limit "$CICHLIDS_MEDIA_LIMIT"
fi

# 5. Keycloak realm import
step "Keycloak realm"
KEYCLOAK_CONTAINER="$(docker compose -f "$COMPOSE_FILE" ps -q keycloak)"
docker exec "$KEYCLOAK_CONTAINER" /opt/keycloak/bin/kcadm.sh config credentials \
  --server http://localhost:8080 --realm master --user admin --password admin >/dev/null

if docker exec "$KEYCLOAK_CONTAINER" /opt/keycloak/bin/kcadm.sh get realms/cichlids >/dev/null 2>&1; then
  echo "Realm 'cichlids' already exists, skipping import."
else
  echo "Importing realm 'cichlids'..."
  docker exec "$KEYCLOAK_CONTAINER" /opt/keycloak/bin/kcadm.sh create realms \
    -f /opt/keycloak/data/import/cichlids-realm.json
fi

# 6. Keycloak account import (creates only missing users and missing profile links)
step "Keycloak account import"
if [ -f "$CICHLIDS_AUTH0_EXPORT" ]; then
  CICHLIDS_ETL_AUTH0_EXPORT="$CICHLIDS_AUTH0_EXPORT" etl keycloak-import
else
  echo "No Auth0 export at $CICHLIDS_AUTH0_EXPORT, skipping."
fi

# 7. Next steps
step "Stack ready"
cat <<'EOF'
Start the API:
  cd app/api/src/Cichlids.Api && ~/.dotnet/dotnet run

Start the app (Expo web):
  cd app/mobile && pnpm install && pnpm exec expo start --web --port 8081

See app/stack/README.md for ports, credentials, and the smoke test.
EOF
