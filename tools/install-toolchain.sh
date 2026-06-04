#!/usr/bin/env bash
# Installs the pinned core platform toolchain (infrastructure, GitOps and policy
# tooling) into /usr/local/bin. It is the single, reproducible installer shared by
# the devcontainer (via postCreate) and the CI `toolchain` job, so both run identical
# tool versions. Idempotent: a tool already present at its pinned version is skipped.
#
# The pinned versions here are the concrete versions installed; the floors that any
# install must satisfy live in tools/toolchain.versions and are enforced by
# tools/verify-toolchain.sh. The .NET, Java, Android and Maestro toolchains are
# heavier and installed separately where the backend/e2e jobs need them.
# Story: task-2.1
set -euo pipefail

# --- pinned core versions ---
TOFU_VERSION="${TOFU_VERSION:-1.9.1}"
KUBECTL_VERSION="${KUBECTL_VERSION:-1.31.4}"
HELM_VERSION="${HELM_VERSION:-3.16.4}"
KUSTOMIZE_VERSION="${KUSTOMIZE_VERSION:-5.5.0}"
SOPS_VERSION="${SOPS_VERSION:-3.9.3}"
AGE_VERSION="${AGE_VERSION:-1.2.1}"
CONFTEST_VERSION="${CONFTEST_VERSION:-0.56.0}"
KIND_VERSION="${KIND_VERSION:-0.25.0}"
KUBECONFORM_VERSION="${KUBECONFORM_VERSION:-0.6.7}"

case "$(uname -m)" in
x86_64) ARCH=amd64; ARCH_X=x86_64 ;;
aarch64 | arm64) ARCH=arm64; ARCH_X=arm64 ;;
*)
  echo "unsupported architecture: $(uname -m)" >&2
  exit 1
  ;;
esac

BIN=/usr/local/bin
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
cd "$TMP"

SUDO=""
[ "$(id -u)" -eq 0 ] || SUDO="sudo"

install_bin() { $SUDO install -m 0755 "$1" "$BIN/$2"; }

have_version() {
  # have_version <command> <expected-substring>
  command -v "$1" >/dev/null 2>&1 && "$1" ${3:---version} 2>&1 | grep -q "$2"
}

echo "Installing core toolchain into $BIN (arch: $ARCH)"

if have_version tofu "v$TOFU_VERSION" version; then echo "  tofu $TOFU_VERSION present"; else
  curl -fsSL -o tofu.zip "https://github.com/opentofu/opentofu/releases/download/v${TOFU_VERSION}/tofu_${TOFU_VERSION}_linux_${ARCH}.zip"
  unzip -oq tofu.zip tofu && install_bin tofu tofu
fi

if have_version kubectl "v$KUBECTL_VERSION" "version --client"; then echo "  kubectl $KUBECTL_VERSION present"; else
  curl -fsSL -o kubectl "https://dl.k8s.io/release/v${KUBECTL_VERSION}/bin/linux/${ARCH}/kubectl" && install_bin kubectl kubectl
fi

if have_version helm "v$HELM_VERSION" version; then echo "  helm $HELM_VERSION present"; else
  curl -fsSL -o helm.tgz "https://get.helm.sh/helm-v${HELM_VERSION}-linux-${ARCH}.tar.gz"
  tar -xzf helm.tgz && install_bin "linux-${ARCH}/helm" helm
fi

if have_version kustomize "v$KUSTOMIZE_VERSION" version; then echo "  kustomize $KUSTOMIZE_VERSION present"; else
  curl -fsSL -o kustomize.tgz "https://github.com/kubernetes-sigs/kustomize/releases/download/kustomize%2Fv${KUSTOMIZE_VERSION}/kustomize_v${KUSTOMIZE_VERSION}_linux_${ARCH}.tar.gz"
  tar -xzf kustomize.tgz && install_bin kustomize kustomize
fi

if have_version sops "$SOPS_VERSION"; then echo "  sops $SOPS_VERSION present"; else
  curl -fsSL -o sops "https://github.com/getsops/sops/releases/download/v${SOPS_VERSION}/sops-v${SOPS_VERSION}.linux.${ARCH}" && install_bin sops sops
fi

if have_version age "v$AGE_VERSION"; then echo "  age $AGE_VERSION present"; else
  curl -fsSL -o age.tgz "https://github.com/FiloSottile/age/releases/download/v${AGE_VERSION}/age-v${AGE_VERSION}-linux-${ARCH}.tar.gz"
  tar -xzf age.tgz && install_bin age/age age && install_bin age/age-keygen age-keygen
fi

if have_version conftest "$CONFTEST_VERSION"; then echo "  conftest $CONFTEST_VERSION present"; else
  curl -fsSL -o conftest.tgz "https://github.com/open-policy-agent/conftest/releases/download/v${CONFTEST_VERSION}/conftest_${CONFTEST_VERSION}_Linux_${ARCH_X}.tar.gz"
  tar -xzf conftest.tgz conftest && install_bin conftest conftest
fi

if have_version kind "v$KIND_VERSION" version; then echo "  kind $KIND_VERSION present"; else
  curl -fsSL -o kind "https://github.com/kubernetes-sigs/kind/releases/download/v${KIND_VERSION}/kind-linux-${ARCH}" && install_bin kind kind
fi

if have_version kubeconform "v$KUBECONFORM_VERSION" -v; then echo "  kubeconform $KUBECONFORM_VERSION present"; else
  curl -fsSL -o kubeconform.tgz "https://github.com/yannh/kubeconform/releases/download/v${KUBECONFORM_VERSION}/kubeconform-linux-${ARCH}.tar.gz"
  tar -xzf kubeconform.tgz kubeconform && install_bin kubeconform kubeconform
fi

echo "Core toolchain installation complete."
