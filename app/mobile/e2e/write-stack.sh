#!/usr/bin/env bash
# Isolated write E2E stack: a second database, bucket, API and web build so write-path E2E specs
# (upload, comments) never touch the seeded dev database, the dev bucket or the dev API. Reuses
# the already-running Postgres, RustFS and Keycloak containers from app/stack/compose.yaml; only
# the database, the bucket, a second API process and a static web build are this script's own.
#
# Usage:
#   write-stack.sh up             bring the write stack up
#   write-stack.sh down           tear it down (idempotent: safe after a partial up, or with
#                                  nothing up at all)
#   write-stack.sh run <command...>   up, run the command with the stack's env exported, then
#                                  always down, even if the command fails
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MOBILE_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$MOBILE_DIR/../.." && pwd)"
API_DIR="$REPO_ROOT/app/api"
COMPOSE_FILE="$REPO_ROOT/app/stack/compose.yaml"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"

# The one database and bucket this script is ever allowed to touch; every drop/delete call is
# guarded against this literal name so a bug elsewhere in the script can never reach the seeded
# dev database or bucket.
DB_NAME="cichlids_e2e"
BUCKET_NAME="cichlids-e2e"

API_PORT=5046
WEB_PORT=8082
API_URL="http://localhost:${API_PORT}"
WEB_URL="http://localhost:${WEB_PORT}"
KEYCLOAK_URL="http://localhost:8180"
DB_CONNECTION="Host=127.0.0.1;Port=5432;Database=${DB_NAME};Username=cichlids;Password=cichlids"

STATE_DIR="${CICHLIDS_WRITE_STACK_STATE_DIR:-/tmp/cichlids-write-stack}"
API_PID_FILE="$STATE_DIR/api.pid"
WEB_PID_FILE="$STATE_DIR/web.pid"
API_LOG="$STATE_DIR/api.log"
WEB_LOG="$STATE_DIR/web.log"
API_BUILD_DIR="$STATE_DIR/api-build"
WEB_EXPORT_DIR="$STATE_DIR/web-e2e"

log() { printf '[write-stack] %s\n' "$1"; }

psql_postgres() {
  docker compose -f "$COMPOSE_FILE" exec -T postgres psql -U cichlids -d postgres -v ON_ERROR_STOP=1 -c "$1"
}

require_stack_running() {
  if ! docker compose -f "$COMPOSE_FILE" exec -T postgres pg_isready -U cichlids -d cichlids >/dev/null 2>&1; then
    echo "Postgres is not reachable via $COMPOSE_FILE; start the dev stack first (app/stack/bootstrap.sh)." >&2
    exit 1
  fi
  # RustFS answers unauthenticated requests with 403 rather than a connection error, so a plain
  # TCP connect (not curl -f, which treats 403 as failure) is the reachability check here.
  if ! (exec 3<>/dev/tcp/127.0.0.1/9000) 2>/dev/null; then
    echo "RustFS is not reachable on 127.0.0.1:9000; start the dev stack first." >&2
    exit 1
  fi
  exec 3>&- 3<&-
  if ! curl -fsS -o /dev/null "$KEYCLOAK_URL/realms/cichlids" 2>/dev/null; then
    echo "Keycloak's cichlids realm is not reachable at $KEYCLOAK_URL; start the dev stack first." >&2
    exit 1
  fi
}

wait_for_http() {
  local url="$1" label="$2" tries="${3:-60}"
  for ((i = 0; i < tries; i++)); do
    if curl -fsS -o /dev/null "$url" 2>/dev/null; then
      return 0
    fi
    sleep 1
  done
  echo "Timed out waiting for $label at $url; see its log for details." >&2
  return 1
}

recreate_database() {
  if [[ "$DB_NAME" != "cichlids_e2e" ]]; then
    echo "Refusing to touch database '$DB_NAME': only cichlids_e2e is allowed here." >&2
    exit 1
  fi

  log "Dropping and recreating database $DB_NAME"
  psql_postgres "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DB_NAME' AND pid <> pg_backend_pid();" >/dev/null
  psql_postgres "DROP DATABASE IF EXISTS $DB_NAME;" >/dev/null
  psql_postgres "CREATE DATABASE $DB_NAME;" >/dev/null
}

drop_database() {
  if [[ "$DB_NAME" != "cichlids_e2e" ]]; then
    echo "Refusing to touch database '$DB_NAME': only cichlids_e2e is allowed here." >&2
    exit 1
  fi

  log "Dropping database $DB_NAME"
  psql_postgres "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DB_NAME' AND pid <> pg_backend_pid();" >/dev/null 2>&1 || true
  psql_postgres "DROP DATABASE IF EXISTS $DB_NAME;" >/dev/null
}

run_migration() {
  log "Applying schema migration to $DB_NAME"
  (cd "$API_DIR" && CICHLIDS_ETL_TARGET_CONNECTION="$DB_CONNECTION" \
    "$DOTNET" run --project src/Cichlids.Etl -c Release -- migrate)
}

bucket_tool() {
  (cd "$SCRIPT_DIR" && "$DOTNET" run bucket-tool.cs -- "$@")
}

build_api() {
  log "Building the API in Release to $API_BUILD_DIR"
  rm -rf "$API_BUILD_DIR"
  (cd "$API_DIR" && "$DOTNET" build src/Cichlids.Api -c Release -o "$API_BUILD_DIR")
}

start_api() {
  build_api

  log "Starting the write-stack API on $API_URL"
  (
    # The content root (where appsettings*.json are read from) defaults to the working directory
    # a .dll is launched from, not the directory the .dll lives in, so this runs from inside the
    # build output rather than from app/api.
    cd "$API_BUILD_DIR"
    ASPNETCORE_URLS="http://0.0.0.0:${API_PORT}" \
    ASPNETCORE_ENVIRONMENT=Development \
    ConnectionStrings__Cichlids="$DB_CONNECTION" \
    ObjectStorage__Bucket="$BUCKET_NAME" \
    ObjectStorage__PublicBaseUrl="http://127.0.0.1:9000/${BUCKET_NAME}" \
    OutboxDispatcher__Enabled=false \
    nohup "$DOTNET" "$API_BUILD_DIR/Cichlids.Api.dll" >"$API_LOG" 2>&1 &
    echo $! >"$API_PID_FILE"
  )
  # Running the built DLL directly (rather than `dotnet run`) never consults
  # Properties/launchSettings.json, so the dev API's applicationUrl cannot override ASPNETCORE_URLS
  # here; no --no-launch-profile equivalent is needed.

  wait_for_http "$API_URL/api/pictures" "the write-stack API (see $API_LOG)"
}

build_web_export() {
  log "Exporting the Expo web build to $WEB_EXPORT_DIR"
  rm -rf "$WEB_EXPORT_DIR"
  # The app imports @cichlids/client-core from its compiled dist/ output, which is not checked in;
  # building it first keeps the export in step with the client-core sources.
  (cd "$MOBILE_DIR" && pnpm --filter @cichlids/client-core build)
  (cd "$MOBILE_DIR" && EXPO_PUBLIC_API_URL="$API_URL" \
    pnpm exec expo export --platform web --output-dir "$WEB_EXPORT_DIR")
}

start_web() {
  build_web_export

  log "Serving the write-stack web build on $WEB_URL"
  nohup node "$SCRIPT_DIR/static-server.mjs" "$WEB_EXPORT_DIR" "$WEB_PORT" >"$WEB_LOG" 2>&1 &
  echo $! >"$WEB_PID_FILE"

  wait_for_http "$WEB_URL" "the write-stack web server (see $WEB_LOG)"
}

# Kills the process recorded in a PID file, but only after confirming it is still the process
# this script started: the PID can get reused by something unrelated, or belong to a second write
# stack started from another checkout sharing the same stack containers, so `kill -0` alone (which
# only checks the PID exists) is not enough. The check reads /proc/$pid/cmdline for the given
# marker (the API's .dll path or the static server's script name); a PID file that fails the check
# is treated as stale and removed without being killed.
stop_pid_file() {
  local pid_file="$1" marker="$2"
  if [[ -f "$pid_file" ]]; then
    local pid
    pid="$(cat "$pid_file")"
    if kill -0 "$pid" 2>/dev/null && tr '\0' '\n' <"/proc/$pid/cmdline" 2>/dev/null | grep -qF "$marker"; then
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
    rm -f "$pid_file"
  fi
}

cmd_up() {
  mkdir -p "$STATE_DIR"
  require_stack_running
  recreate_database
  run_migration
  bucket_tool ensure "$BUCKET_NAME"
  start_api
  start_web
  log "Write stack up: API=$API_URL WEB=$WEB_URL DB=$DB_NAME BUCKET=$BUCKET_NAME"
}

cmd_down() {
  mkdir -p "$STATE_DIR"
  stop_pid_file "$WEB_PID_FILE" "static-server.mjs"
  stop_pid_file "$API_PID_FILE" "Cichlids.Api.dll"
  rm -rf "$WEB_EXPORT_DIR" "$API_BUILD_DIR"
  bucket_tool delete "$BUCKET_NAME"
  drop_database
  log "Write stack down."
}

cmd_run() {
  if [[ $# -eq 0 ]]; then
    echo "Usage: write-stack.sh run <command...>" >&2
    exit 2
  fi

  trap cmd_down EXIT
  cmd_up

  E2E_API_URL="$API_URL" \
  E2E_WEB_URL="$WEB_URL" \
  E2E_KEYCLOAK_URL="$KEYCLOAK_URL" \
  E2E_DB="$DB_NAME" \
  "$@"
}

case "${1:-}" in
  up)
    cmd_up
    ;;
  down)
    cmd_down
    ;;
  run)
    shift
    cmd_run "$@"
    ;;
  *)
    echo "Usage: write-stack.sh <up|down|run <command...>>" >&2
    exit 2
    ;;
esac
