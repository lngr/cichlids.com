---
id: TASK-2.26
title: Replace the repository-local secret scanner with gitleaks
status: In Review
assignee: []
created_date: '2026-09-28 12:45'
updated_date: '2026-10-13 08:16'
labels: []
dependencies: []
references:
  - tools/test-pre-push-hook.sh
  - .gitleaks.toml
  - .github/workflows/ci.yml
parent_task_id: TASK-2
priority: medium
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Secret scanning uses gitleaks, the established open-source scanner, in the pre-push hook and in the CI secrets-scan job. The repository-local detector `tools/check-no-plaintext-secrets.py` is retired.

### Why
gitleaks ships maintained rules for real token formats, entropy checks, a versioned configuration file (`.gitleaks.toml`), inline allow markers (`gitleaks:allow`) and a scan of the full git history. It is a single static binary, runs offline and can be pinned like the rest of the toolchain. The history scan matters for this repository because its history was rewritten to remove leaked credentials.

### Scope
- Pin gitleaks in `tools/toolchain.versions`, `tools/install-toolchain.sh` and the `Brewfile`.
- `.gitleaks.toml` with the repository defaults, excluding `workspace-legacy/` (recovered legacy credentials, treated as compromised) and lock files.
- Pre-push hook and CI job run gitleaks; the CI job additionally scans the full history.
- Retire `tools/check-no-plaintext-secrets.py` and its self-test step; adjust the acceptance criteria of TASK-2.3 and TASK-2.15 that name it.
- Inline markers written as `gitleaks:allow` stay valid unchanged.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a commit that adds a string in the format of a real GitHub personal access token, When the operator runs git push, Then the pre-push hook refuses the push and gitleaks names the file and line
- [ ] #2 Given the same commit pushed with --no-verify to a throwaway branch, When GitHub Actions runs, Then the secrets-scan job fails and its log shows the gitleaks finding
- [ ] #3 Given the tracked files on the branch, When the operator runs gitleaks against the working tree, Then it reports zero findings and every dev or test credential line carries a gitleaks:allow marker
- [ ] #4 Given the full git history of main, When the operator runs gitleaks in history mode, Then every finding is either absent or listed and triaged in the task notes
- [ ] #5 Given the devcontainer and the operator host, When the operator runs gitleaks version, Then both print the version pinned in tools/toolchain.versions
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach (test-first)
- Red first: a hook test (shell script under `tools/`) that creates a throwaway repository with
  a commit holding a string in the format of a GitHub personal access token and asserts that the
  pre-push hook refuses; it fails while the hook still calls the old detector for a case only
  gitleaks catches, then passes with gitleaks.

### Toolchain
- Pin gitleaks v8.30.1: `GITLEAKS_MIN` in `tools/toolchain.versions`, download with checksum
  verification in `tools/install-toolchain.sh`, `tools/verify-toolchain.sh`, `Brewfile`, the
  devcontainer image if it installs the toolchain separately.

### Configuration
- `.gitleaks.toml`: extends the default rules, allowlists `workspace-legacy/` (recovered legacy
  credentials, treated as compromised) and lock files (`pnpm-lock.yaml`, `package-lock.json`,
  `packages.lock.json`).
- Existing `gitleaks:allow` markers stay; every dev or test credential line that gitleaks reports
  gets one.

### Hook and CI
- `.githooks/pre-push`: `gitleaks git` over the commits being pushed (from the hook input), plus
  `gitleaks dir` of the working tree if cheap; refuses with the finding (file and line).
- CI job `secrets-scan`: gitleaks over the working tree and the full history (`fetch-depth: 0`);
  the hook wiring check stays.
- Retire `tools/check-no-plaintext-secrets.py` and its self-test step; in TASK-2.3 and TASK-2.15
  replace the name of the old detector in the acceptance criteria by gitleaks, nothing else.

### History triage
- `gitleaks git` over the full history of main; every finding listed in the task notes with its
  triage (false positive, dev value, revoked credential).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- gitleaks 8.30.1: `GITLEAKS_MIN=8.30.0` in `tools/toolchain.versions`, checksum-verified
  install of the linux x64 or arm64 archive in `tools/install-toolchain.sh`, check in
  `tools/verify-toolchain.sh`, `brew "gitleaks"` in the `Brewfile`.
- `.gitleaks.toml`: default rules; allowlists `workspace-legacy/`, lock files and the
  `generic-api-key` rule in `*.env.example` (empty values that the rule reads across lines).
- `.githooks/pre-push`: `gitleaks git --redact` over exactly the pushed commits (new refs against
  the remote refs); fails when gitleaks is missing. No working-tree scan in the hook, because
  `gitleaks dir` also reads untracked and ignored files.
- CI `secrets-scan`: full history checkout, toolchain, hook test, `gitleaks dir .`,
  `gitleaks git .` and the hook wiring check (`tools/check-git-hooks.py` requires gitleaks in the
  hook).
- `tools/check-no-plaintext-secrets.py` is removed (in commit 8e2a463 together with the CI change).

### Verified by the agent
- `tools/test-pre-push-hook.sh` (throwaway repository, bare fake remote): red before the hook
  change, then 8 of 8 checks: clean push accepted; token-shaped string refused with rule, file and
  line and a redacted value; new branch with the secret refused; `workspace-legacy/` and a
  `gitleaks:allow` line accepted; branch delete not blocked.
- Tracked files (export of all tracked files): no leaks, also with `--ignore-gitleaks-allow`.
- History: `gitleaks git` over 390 commits of main reports no leaks, with and without the config.
  Nothing to triage (AC #4).
- `verify-toolchain.sh core` prints gitleaks 8.30.1 in the devcontainer.

### Open
- AC #2 (CI run on GitHub with a planted token on a throwaway branch) and the host half of AC #5
  (`brew install gitleaks`, `gitleaks version`) need the operator.
- AC #2 of TASK-2.15 names the removed detector; its wording is a decision of the project owner.
- TASK-2.6 lists the old detector as part of its verifying artifact; to align when TASK-2.6
  starts. The plans and notes of TASK-2.3, 2.15 and 2.28 describe the detector as it was.
<!-- SECTION:NOTES:END -->
