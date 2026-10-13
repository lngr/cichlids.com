#!/usr/bin/env bash
# Installs Argo CD at the pinned release into the argocd namespace, waits for it to roll out and
# applies the root Application that hands the cluster over to reconciliation from Git.
# Idempotent: every step is a declarative apply.
# Usage: install-argocd.sh <argocd-version> <root-application-manifest> [argocd-overlay]
# Story: task-2.4
set -euo pipefail

VERSION="${1:?usage: install-argocd.sh <argocd-version> <root-application-manifest>}"
ROOT_MANIFEST="${2:?usage: install-argocd.sh <argocd-version> <root-application-manifest>}"
OVERLAY="${3:-$(dirname "$0")/argocd}"
[[ "$VERSION" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Invalid Argo CD release" >&2; exit 1; }
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cp "$OVERLAY/"*.yaml "$WORK/"
sed -i "s@https://raw.githubusercontent.com/argoproj/argo-cd/[^/]*/manifests/install.yaml@https://raw.githubusercontent.com/argoproj/argo-cd/${VERSION}/manifests/install.yaml@" "$WORK/kustomization.yaml"
KUBECTL=/usr/local/bin/kubectl
export KUBECONFIG=/etc/rancher/k3s/k3s.yaml

"$KUBECTL" create namespace argocd --dry-run=client -o yaml | "$KUBECTL" apply -f -
"$KUBECTL" apply --server-side --force-conflicts -n argocd \
  -k "$WORK"

for deployment in $("$KUBECTL" -n argocd get deployments -o name); do
  "$KUBECTL" -n argocd rollout status "$deployment" --timeout=300s
done
"$KUBECTL" -n argocd rollout status statefulset/argocd-application-controller --timeout=300s

"$KUBECTL" apply -f "$ROOT_MANIFEST"
