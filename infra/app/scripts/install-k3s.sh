#!/usr/bin/env bash
# Installs single-node k3s at the pinned release on this node and waits until the node is Ready.
# Reconciles the config file and service arguments before waiting for readiness.
# Usage: install-k3s.sh <k3s-version>   (for example v1.36.5+k3s1)
# Story: task-2.4
set -euo pipefail

VERSION="${1:?usage: install-k3s.sh <k3s-version>}"
KUBECTL=/usr/local/bin/kubectl
export KUBECONFIG=/etc/rancher/k3s/k3s.yaml

CONFIG=/etc/rancher/k3s/config.yaml
SERVICE=/etc/systemd/system/k3s.service
PENDING="${CONFIG}.restart-required"
mkdir -p "$(dirname "$CONFIG")"
DESIRED=$(mktemp)
trap 'rm -f "$DESIRED"' EXIT
printf 'write-kubeconfig-mode: "0600"\n' > "$DESIRED"
CONFIG_CHANGED=false
if ! cmp -s "$DESIRED" "$CONFIG"; then
  install -m 600 /dev/null "$PENDING"
  install -m 600 "$DESIRED" "$CONFIG"
  CONFIG_CHANGED=true
fi
chmod 600 "$CONFIG"

VERSION_MATCHES=false
if command -v k3s >/dev/null 2>&1 && k3s --version | awk '{print $3}' | grep -qxF "$VERSION"; then
  VERSION_MATCHES=true
fi
LEGACY_FLAGS=false
if tr -d "'\\\"\\\\\n" < "$SERVICE" 2>/dev/null | grep -qE -- '--disable[=[:space:]]+traefik'; then
  LEGACY_FLAGS=true
fi

if ! "$VERSION_MATCHES" || "$CONFIG_CHANGED" || "$LEGACY_FLAGS" || [[ -f "$PENDING" ]]; then
  install -m 600 /dev/null "$PENDING"
  # The installer reconciles the systemd unit; server settings come from CONFIG.
  curl -sfL "https://raw.githubusercontent.com/k3s-io/k3s/${VERSION//+/%2B}/install.sh" |
    INSTALL_K3S_VERSION="$VERSION" \
      INSTALL_K3S_EXEC="server" \
      INSTALL_K3S_SKIP_START=true \
      sh -s -
  systemctl restart k3s
  rm -f "$PENDING"
else
  echo "k3s $VERSION config and service are current"
fi

# The node object appears shortly after the service starts.
for _ in $(seq 1 60); do
  if "$KUBECTL" get nodes 2>/dev/null | grep -q .; then
    break
  fi
  sleep 5
done
"$KUBECTL" wait --for=condition=Ready node --all --timeout=300s
