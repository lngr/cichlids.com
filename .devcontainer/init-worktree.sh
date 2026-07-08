#!/usr/bin/env bash
set -euo pipefail

# Automatically initialize worktree with unique devcontainer configuration.
# Called by devcontainer.json initializeCommand before container creation.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# Get the directory name (worktree or project root)
WORKTREE_NAME="$(basename "$PROJECT_DIR")"

# Sanitize: lowercase, only alphanum + hyphens
SANITIZED_NAME="$(echo "$WORKTREE_NAME" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9-]/-/g; s/--*/-/g; s/^-//; s/-$//')"

# Mount und Scope werden zur Scaffold-Zeit gebacken; eine Env-Var überschreibt
# den Scope für einen einzelnen Start (gesetzt vom Launcher).
WORKSPACE_MOUNT="${WORKSPACE_MOUNT:-parent}"
CONTAINER_SCOPE="${CONTAINER_SCOPE:-shared}"

# project-Mount bindet jedes Verzeichnis an seinen eigenen Container.
if [ "$WORKSPACE_MOUNT" = "project" ]; then
  CONTAINER_SCOPE="per-worktree"
fi

# Compose-Projektname aus dem effektiven Scope.
if [ "$CONTAINER_SCOPE" = "shared" ] || [ "$SANITIZED_NAME" = "cichlids" ]; then
  COMPOSE_PROJECT_NAME="devcontainer-cichlids"
else
  COMPOSE_PROJECT_NAME="devcontainer-cichlids-${SANITIZED_NAME}"
fi

# Update COMPOSE_PROJECT_NAME in .env (preserve other variables)
ENV_FILE="$SCRIPT_DIR/.env"
if [ -f "$ENV_FILE" ]; then
  sed -i '/^COMPOSE_PROJECT_NAME=/d' "$ENV_FILE"
else
  touch "$ENV_FILE"
fi
echo "COMPOSE_PROJECT_NAME=${COMPOSE_PROJECT_NAME}" >> "$ENV_FILE"

# Host-Identität festhalten, damit der Container-User dem Host-User entspricht.
for _host_var in HOST_USER HOST_UID HOST_GID HOST_HOME; do
  sed -i "/^${_host_var}=/d" "$ENV_FILE" 2>/dev/null || true
done
{
  echo "HOST_USER=$(id -un)"
  echo "HOST_UID=$(id -u)"
  echo "HOST_GID=$(id -g)"
  echo "HOST_HOME=$HOME"
} >> "$ENV_FILE"

echo "Devcontainer initialized: COMPOSE_PROJECT_NAME=${COMPOSE_PROJECT_NAME}"

# Generate empty docker-compose.local.yml stub if not present
LOCAL_COMPOSE="$SCRIPT_DIR/docker-compose.local.yml"
if [ ! -f "$LOCAL_COMPOSE" ]; then
  cat > "$LOCAL_COMPOSE" <<'STUBEOF'
# Personal docker-compose overrides (this file is gitignored).
services: {}
STUBEOF
  echo "Generated empty docker-compose.local.yml stub"
fi

# Generate docker-compose.workspace.yml stub for parent workspace mount
WORKSPACE_COMPOSE="$SCRIPT_DIR/docker-compose.workspace.yml"
cat > "$WORKSPACE_COMPOSE" <<'COMPOSEOF'
services: {}
COMPOSEOF
echo "Generated docker-compose.workspace.yml stub"
