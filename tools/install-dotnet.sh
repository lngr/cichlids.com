#!/usr/bin/env bash
# Installs the .NET SDK pinned in app/api/global.json into ~/.dotnet with Microsoft's
# dotnet-install script. ~/.dotnet is the path app/stack/bootstrap.sh and the stack README
# expect. A symlink in ~/.local/bin puts dotnet on the PATH for tools/verify-toolchain.sh.
# Idempotent: an SDK already present at the pinned version is skipped.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
global_json="$here/../app/api/global.json"
install_dir="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"

version="$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]+"' "$global_json" | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')"
if [ -z "$version" ]; then
  echo "no SDK version found in $global_json" >&2
  exit 1
fi

if [ -x "$install_dir/dotnet" ] && "$install_dir/dotnet" --list-sdks 2>/dev/null | grep -q "^$version "; then
  echo "  .NET SDK $version present in $install_dir"
else
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  curl -fsSL -o "$tmp/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh
  bash "$tmp/dotnet-install.sh" --version "$version" --install-dir "$install_dir" --no-path
fi

mkdir -p "$HOME/.local/bin"
ln -sfn "$install_dir/dotnet" "$HOME/.local/bin/dotnet"

"$install_dir/dotnet" --list-sdks
