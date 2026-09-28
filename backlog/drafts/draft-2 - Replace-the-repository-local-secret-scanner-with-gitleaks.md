---
id: DRAFT-2
title: Replace the repository-local secret scanner with gitleaks
status: Draft
assignee: []
created_date: '2026-09-28 12:45'
labels: []
dependencies: []
parent_task_id: task-2
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
