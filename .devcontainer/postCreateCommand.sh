#!/usr/bin/env bash
set -euo pipefail

# postCreateCommand – runs once after devcontainer is created.
# Sets up timezone, workspace permissions, and calls personal overlay hook.

# Ensure devcontainer feature PATH (e.g. Node via nvm) is loaded
if [ -f /etc/profile ]; then
  # shellcheck disable=SC1091
  . /etc/profile || true
fi
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:${PATH:-}"
export PATH="$HOME/.local/bin:$PATH"

die() {
  echo "ERROR: $*" >&2
  exit 1
}

maybe_add_nvm_node_to_path() {
  local base bin_dir
  for base in "${NVM_DIR:-}" "/usr/local/share/nvm" "$HOME/.nvm"; do
    [ -n "$base" ] || continue
    bin_dir="$(ls -1d "$base/versions/node"/*/bin 2>/dev/null | sort -V | tail -n 1 || true)"
    if [ -n "$bin_dir" ] && [ -x "$bin_dir/node" ]; then
      export PATH="$bin_dir:$PATH"
      return 0
    fi
  done
  return 1
}

maybe_add_nvm_node_to_path || true

: "${TZ:=Europe/Berlin}"
export TZ

# Ensure /workspaces is writable for git worktrees
sudo chown "$(id -u):$(id -g)" /workspaces 2>/dev/null || true

# Setup timezone
if [ -f "/usr/share/zoneinfo/$TZ" ]; then
  sudo ln -snf "/usr/share/zoneinfo/$TZ" /etc/localtime 2>/dev/null || true
  echo "$TZ" | sudo tee /etc/timezone >/dev/null 2>/dev/null || true
fi

# Provide the Backlog.md CLI (user stories / spec management; see docs/adr/0015).
# Installed into $HOME/.local (already on PATH above) so it is available to humans
# in the container and as the `backlog mcp` server the coding agent uses via .mcp.json.
if ! command -v backlog >/dev/null 2>&1; then
  echo "Installing Backlog.md CLI..."
  NPM_CONFIG_PREFIX="$HOME/.local" npm i -g --progress=false --loglevel=warn backlog.md \
    || echo "WARNING: backlog.md install failed (network?). Install later with: npm i -g backlog.md"
fi

# --- Personal overlay hook ---
if [ -f ".devcontainer/postCreateCommand.local.sh" ]; then
  echo "=== Running personal postCreate hook ==="
  bash .devcontainer/postCreateCommand.local.sh
fi
