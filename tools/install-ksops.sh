#!/usr/bin/env bash
# Install KSOPS from the pinned upstream release with a checked-in SHA-256 checksum.
set -euo pipefail
VERSION=4.5.1
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DEST="${KSOPS_INSTALL_DIR:-/usr/local/bin}"
case "$(uname -m)" in
  x86_64) ARCH=x86_64 ;;
  aarch64 | arm64) ARCH=arm64 ;;
  *) echo "Unsupported KSOPS architecture" >&2; exit 1 ;;
esac
ARCHIVE="ksops_${VERSION}_Linux_${ARCH}.tar.gz"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cd "$WORK"
curl -fsSL -o "$ARCHIVE" "https://github.com/viaduct-ai/kustomize-sops/releases/download/v${VERSION}/${ARCHIVE}"
grep " ${ARCHIVE}\$" "$SCRIPT_DIR/ksops-checksums.txt" | sha256sum -c -
tar -xzf "$ARCHIVE" ksops
if [[ -w "$DEST" ]]; then
  install -m 0755 ksops "$DEST/ksops"
else
  sudo install -m 0755 ksops "$DEST/ksops"
fi
echo "KSOPS ${VERSION} installed"
