#!/usr/bin/env bash
# Installs single-node k3s at the pinned release on this node and waits until the node is Ready.
# Idempotent: an installation that already reports the pinned version is left untouched.
# Usage: install-k3s.sh <k3s-version>   (for example v1.36.5+k3s1)
# Story: task-2.4
set -euo pipefail

VERSION="${1:?usage: install-k3s.sh <k3s-version>}"
KUBECTL=/usr/local/bin/kubectl
export KUBECONFIG=/etc/rancher/k3s/k3s.yaml

if command -v k3s >/dev/null 2>&1 && k3s --version | grep -qF "$VERSION"; then
  echo "k3s $VERSION is already installed"
else
  # The install script of the tagged release; the bundled Traefik is disabled because
  # ingress-nginx is the ingress controller (ADR-0010).
  curl -sfL "https://raw.githubusercontent.com/k3s-io/k3s/${VERSION//+/%2B}/install.sh" |
    INSTALL_K3S_VERSION="$VERSION" \
      INSTALL_K3S_EXEC="server --disable traefik --write-kubeconfig-mode 600" \
      sh -s -
fi

# The node object appears shortly after the service starts.
for _ in $(seq 1 60); do
  if "$KUBECTL" get nodes 2>/dev/null | grep -q .; then
    break
  fi
  sleep 5
done
"$KUBECTL" wait --for=condition=Ready node --all --timeout=300s
