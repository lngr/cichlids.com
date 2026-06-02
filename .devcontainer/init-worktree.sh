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

# Build unique compose project name
COMPOSE_PROJECT_NAME="devcontainer-cichlids-${SANITIZED_NAME}"

# Update COMPOSE_PROJECT_NAME in .env (preserve other variables)
ENV_FILE="$SCRIPT_DIR/.env"
if [ -f "$ENV_FILE" ]; then
  sed -i '/^COMPOSE_PROJECT_NAME=/d' "$ENV_FILE"
else
  touch "$ENV_FILE"
fi
echo "COMPOSE_PROJECT_NAME=${COMPOSE_PROJECT_NAME}" >> "$ENV_FILE"

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
