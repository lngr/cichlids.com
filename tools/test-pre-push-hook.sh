#!/usr/bin/env bash
# Exercises .githooks/pre-push end to end in a throwaway repository with a bare repository as
# remote: a clean push goes through, a push whose commits hold a string in the format of a
# GitHub personal access token is refused with the finding named by file and line (value
# redacted), the same string below workspace-legacy/ or on a gitleaks allow line is accepted,
# and a secret in an earlier commit of a new branch is found as well. Needs gitleaks on PATH.
# Story: task-2.26
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$here/.." && pwd)"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Assembled at runtime so this file does not contain a token-shaped string itself.
token="ghp_$(printf '%s' 'aB3dE5fG7hJ9kL1mN3pQ5rS7tU9vW1xY3zA5')"
allow_marker="gitleaks:allow"

failures=0
fail() {
  echo "FAIL: $1" >&2
  failures=$((failures + 1))
}
pass() { echo "ok:   $1"; }

git init -q --bare "$work/remote.git"
git init -q -b main "$work/repo"
cd "$work/repo"
git config user.email "test@example.invalid"
git config user.name "Hook Test"
git remote add origin "$work/remote.git"
mkdir .githooks
cp "$repo_root/.githooks/pre-push" .githooks/pre-push
[ -f "$repo_root/.gitleaks.toml" ] && cp "$repo_root/.gitleaks.toml" .gitleaks.toml
git config core.hooksPath .githooks

commit_file() {
  # commit_file <path> <content line>
  mkdir -p "$(dirname "$1")"
  printf '%s\n' "$2" >>"$1"
  git add "$1"
  git commit -q -m "add $1"
}

commit_file README.md "# throwaway"
if git push -q origin main >"$work/out" 2>&1; then pass "clean push is accepted"; else
  fail "clean push was refused"
  sed 's/^/      /' "$work/out" >&2
fi

git checkout -q -b leak
commit_file config/settings.txt "line one"
commit_file config/settings.txt "github_pat=$token"
if git push -q origin leak >"$work/out" 2>&1; then fail "push with a token-shaped string was accepted"; else
  pass "push with a token-shaped string is refused"
  if grep -q "config/settings.txt" "$work/out" && grep -qi "line" "$work/out"; then pass "finding names file and line"; else
    fail "finding does not name file and line"
  fi
  if grep -q "$token" "$work/out"; then fail "refusal output shows the secret value"; else pass "secret value is redacted"; fi
fi

git checkout -q -b follow-up
commit_file docs/note.txt "harmless"
if git push -q origin follow-up >"$work/out" 2>&1; then fail "new branch containing the secret commit was accepted"; else
  pass "new branch containing the secret commit is refused"
fi

git checkout -q main
git checkout -q -b legacy
commit_file workspace-legacy/old.conf "github_pat=$token"
if git push -q origin legacy >"$work/out" 2>&1; then pass "token below workspace-legacy is accepted"; else
  fail "token below workspace-legacy was refused"
fi

git checkout -q main
git checkout -q -b allowed
commit_file dev/local.txt "github_pat=$token # $allow_marker"
if git push -q origin allowed >"$work/out" 2>&1; then pass "allow-marked line is accepted"; else
  fail "allow-marked line was refused"
fi

git push -q origin --delete allowed >/dev/null 2>&1 && pass "branch deletion is not blocked" || fail "branch deletion was refused"

if [ "$failures" -gt 0 ]; then
  echo "pre-push hook test FAILED ($failures)" >&2
  exit 1
fi
echo "pre-push hook test OK"
