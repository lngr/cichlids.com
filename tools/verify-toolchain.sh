#!/usr/bin/env bash
# Asserts that the platform toolchain is present at or above the minimum versions
# pinned in tools/toolchain.versions. It is the single check used both by developers
# in the devcontainer and by the CI `toolchain` job, so "the tools are right" is
# mechanically verifiable rather than assumed.
#
# Scopes let callers verify only the subset they need:
#   core     infrastructure, GitOps and policy tooling (default for the toolchain gate)
#   backend  the .NET build/test toolchain
#   e2e      the native Android + Maestro driving toolchain
#   all      every scope (default when no scope is given)
#
# Usage: tools/verify-toolchain.sh [core|backend|e2e|all]
# Exits non-zero and names every tool that is missing or below its minimum.
# Story: task-2.1
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
. "$here/toolchain.versions"

scope="${1:-all}"
case "$scope" in
core | backend | e2e | all) ;;
*)
  echo "unknown scope: $scope (expected core|backend|e2e|all)" >&2
  exit 2
  ;;
esac

failures=()

# Extract the first dotted version number from a tool's --version output.
extract_version() {
  grep -oE '[0-9]+(\.[0-9]+)+' | head -n1
}

# True when $1 (found) is greater than or equal to $2 (minimum) under version sort.
version_ge() {
  [ "$(printf '%s\n%s\n' "$2" "$1" | sort -V | head -n1)" = "$2" ]
}

# check <label> <min> <command...>
# Runs the command to obtain a version, compares against the minimum, records the result.
check() {
  local label="$1" min="$2"
  shift 2
  local bin="$1"
  if ! command -v "$bin" >/dev/null 2>&1; then
    printf '  %-14s %s\n' "$label" "MISSING (need >= $min)"
    failures+=("$label: not installed (need >= $min)")
    return
  fi
  local out found
  out="$("$@" 2>&1 || true)"
  found="$(printf '%s' "$out" | extract_version || true)"
  if [ -z "$found" ]; then
    printf '  %-14s %s\n' "$label" "present, version unparsable"
    failures+=("$label: version not parseable from '$*'")
    return
  fi
  if version_ge "$found" "$min"; then
    printf '  %-14s %s\n' "$label" "OK $found (>= $min)"
  else
    printf '  %-14s %s\n' "$label" "TOO OLD $found (< $min)"
    failures+=("$label: $found is below minimum $min")
  fi
}

check_core() {
  echo "core (infrastructure, GitOps, policy):"
  check tofu "$TOFU_MIN" tofu version
  check kubectl "$KUBECTL_MIN" kubectl version --client
  check helm "$HELM_MIN" helm version --short
  check kustomize "$KUSTOMIZE_MIN" kustomize version
  check sops "$SOPS_MIN" sops --version --disable-version-check
  check age "$AGE_MIN" age --version
  check conftest "$CONFTEST_MIN" conftest --version
  check kind "$KIND_MIN" kind --version
  check kubeconform "$KUBECONFORM_MIN" kubeconform -v
  check node "$NODE_MIN" node --version
}

check_backend() {
  echo "backend (.NET):"
  check dotnet "$DOTNET_MIN" dotnet --version
}

check_e2e() {
  echo "e2e (Android, Maestro):"
  check java "$JAVA_MIN" java -version
  check sdkmanager "$ANDROID_CMDLINE_MIN" sdkmanager --version
  check maestro "$MAESTRO_MIN" maestro --version
}

if [ "$scope" = core ] || [ "$scope" = all ]; then check_core; fi
if [ "$scope" = backend ] || [ "$scope" = all ]; then check_backend; fi
if [ "$scope" = e2e ] || [ "$scope" = all ]; then check_e2e; fi

echo
if [ "${#failures[@]}" -gt 0 ]; then
  echo "toolchain incomplete (${#failures[@]} issue(s)):" >&2
  for f in "${failures[@]}"; do echo "  - $f" >&2; done
  exit 1
fi
echo "toolchain OK (scope: $scope)"
