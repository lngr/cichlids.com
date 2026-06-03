#!/usr/bin/env bash
set -euo pipefail

# postStartCommand – runs on every devcontainer start.
# Sets up localhost port forwards and calls personal overlay hook.

if [ -f /etc/profile ]; then
  # shellcheck disable=SC1091
  . /etc/profile || true
fi
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:${PATH:-}"
export PATH="$HOME/.local/bin:$PATH"

# Start the Backlog.md web UI as a background service (see docs/adr/0015).
# Coding agents use the MCP server; the web UI is the human board and is reachable
# from the host via the port forwarded in devcontainer.json. Idempotent across restarts.
start_backlog_browser() {
  local port=6480 pidfile="/tmp/backlog-browser.pid"
  command -v backlog >/dev/null 2>&1 || return 0
  if [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile" 2>/dev/null)" >/dev/null 2>&1; then
    return 0
  fi
  if command -v ss >/dev/null 2>&1 && ss -ltn "sport = :${port}" 2>/dev/null | grep -q LISTEN; then
    return 0
  fi
  setsid backlog browser --port "$port" --no-open >/tmp/backlog-browser.log 2>&1 &
  local pid=$!
  disown "$pid" 2>/dev/null || true
  echo "$pid" >"$pidfile"
  echo "Started Backlog.md web UI on port ${port} (pid ${pid})."
}
start_backlog_browser

# Port forward helpers for services running in sibling containers
if ! command -v socat >/dev/null 2>&1; then
  echo "WARN: socat not found; skipping localhost port forwards." >&2
  exit 0
fi

is_pid_running() {
  local pid="$1"
  [ -n "$pid" ] && kill -0 "$pid" >/dev/null 2>&1
}

is_listening() {
  local port="$1"
  if command -v ss >/dev/null 2>&1; then
    ss -ltn "sport = :${port}" 2>/dev/null | grep -q LISTEN
    return $?
  fi
  return 1
}

start_forward() {
  local name="$1" bind_addr="$2" local_port="$3" target_host="$4" target_port="$5"
  local pidfile="/tmp/${name}.pid"

  if [ -f "$pidfile" ]; then
    local existing_pid
    existing_pid="$(cat "$pidfile" 2>/dev/null || true)"
    if is_pid_running "$existing_pid"; then
      return 0
    fi
  fi

  if is_listening "$local_port"; then
    return 0
  fi

  setsid socat \
    "TCP-LISTEN:${local_port},fork,reuseaddr,bind=${bind_addr}" \
    "TCP:${target_host}:${target_port}" \
    >/dev/null 2>&1 &

  local pid=$!
  disown "$pid" 2>/dev/null || true
  echo "$pid" >"$pidfile"
}

# No service port forwards configured

# --- Personal overlay hook ---
if [ -f ".devcontainer/postStartCommand.local.sh" ]; then
  echo "=== Running personal postStart hook ==="
  bash .devcontainer/postStartCommand.local.sh
fi
