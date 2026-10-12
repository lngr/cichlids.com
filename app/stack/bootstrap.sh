#!/usr/bin/env bash
# Brings the local MVP stack to the state a first legacy import starts from and runs that import:
# Docker services, legacy MySQL reachability, the ETL reset (empty database, realm users without
# the admin role deleted), a fresh import of the cichlids realm from its file, the full ETL run,
# the media seed, the legacy account import into Keycloak and a final verification. Every run
# starts with the reset, so rows created by hand in the dev database do not survive it; media
# objects in the bucket stay. The script fails when Postgres does not become ready within
# CICHLIDS_POSTGRES_WAIT_SECONDS (default 60) and ends by printing the next steps (starting the
# API and the app).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
API_DIR="$REPO_ROOT/app/api"
COMPOSE_FILE="$SCRIPT_DIR/compose.yaml"

CICHLIDS_LEGACY_STACK_DIR="${CICHLIDS_LEGACY_STACK_DIR:-$REPO_ROOT/workspace-legacy/legacy-stack}"
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

etl() {
  (cd "$API_DIR" && DOTNET_ENVIRONMENT=Development "$DOTNET" run --project src/Cichlids.Etl -c Release -- "$@")
}

# 1. Postgres, RustFS, Keycloak
step "Docker services (postgres, rustfs, keycloak)"
"$SCRIPT_DIR/services-up.sh"

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

# 3. Reset: empty database with every migration applied, realm users without the admin role
# deleted, self-registration closed
step "ETL reset"
etl reset

# 4. Keycloak realm: deleted and imported from its file, which holds the dev users
step "Keycloak realm"
KEYCLOAK_CONTAINER="$(docker compose -f "$COMPOSE_FILE" ps -q keycloak)"
docker exec "$KEYCLOAK_CONTAINER" /opt/keycloak/bin/kcadm.sh config credentials \
  --server http://localhost:8080 --realm master --user admin --password admin >/dev/null

if docker exec "$KEYCLOAK_CONTAINER" /opt/keycloak/bin/kcadm.sh get realms/cichlids >/dev/null 2>&1; then
  echo "Deleting realm 'cichlids'..."
  docker exec "$KEYCLOAK_CONTAINER" /opt/keycloak/bin/kcadm.sh delete realms/cichlids
fi
echo "Importing realm 'cichlids'..."
docker exec "$KEYCLOAK_CONTAINER" /opt/keycloak/bin/kcadm.sh create realms \
  -f /opt/keycloak/data/import/cichlids-realm.json

# 5. Full ETL run into the empty database, with its own verification pass
step "ETL all steps"
etl all

# 6. Media
step "Media seed (limit=${CICHLIDS_MEDIA_LIMIT})"
if [ "$CICHLIDS_MEDIA_LIMIT" = "0" ]; then
  etl media
else
  etl media --limit "$CICHLIDS_MEDIA_LIMIT"
fi

# 7. Keycloak account import; the realm opens self-registration only after this import has
# created every account
step "Keycloak account import"
if [ -f "$CICHLIDS_AUTH0_EXPORT" ]; then
  CICHLIDS_ETL_AUTH0_EXPORT="$CICHLIDS_AUTH0_EXPORT" etl keycloak-import --open-registration
else
  echo "No Auth0 export at $CICHLIDS_AUTH0_EXPORT, skipping; self-registration stays closed."
fi

# 8. Verification of the finished stand
step "ETL verify"
etl verify

# 9. Next steps
step "Stack ready"
cat <<'EOF'
Start the API:
  cd app/api/src/Cichlids.Api && ~/.dotnet/dotnet run

Start the app (Expo web):
  cd app/mobile && pnpm install && pnpm exec expo start --web --port 8081

See app/stack/README.md for ports, credentials, and the smoke test.
EOF
