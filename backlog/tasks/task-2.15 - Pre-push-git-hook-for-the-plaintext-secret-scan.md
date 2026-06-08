---
id: TASK-2.15
title: Pre-push git hook for the plaintext-secret scan
status: In Progress
assignee:
  - claude
created_date: '2026-06-05 20:29'
updated_date: '2026-06-05 20:30'
labels:
  - infra
milestone: m-0
dependencies: []
references:
  - tools/check-git-hooks.py
parent_task_id: TASK-2
ordinal: 17000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Catch a plaintext secret before it leaves the machine, not only in CI (where it is already
pushed). Adds a local pre-push hook as early warning; CI secrets-scan stays the enforced
backstop (hooks are local and bypassable with --no-verify).

### Scope
- .githooks/pre-push: runs tools/check-no-plaintext-secrets.py and blocks the push on findings.
- Activate committed hooks via core.hooksPath=.githooks in the devcontainer postCreate; documented for other contributors.
- tools/check-git-hooks.py: asserts the hook exists, is executable, runs the scanner, and that postCreate wires core.hooksPath. Self-test + CI step (in secrets-scan job).

### Affected artifacts
- .githooks/pre-push (new), .devcontainer/postCreateCommand.sh, tools/check-git-hooks.py (new), .github/workflows/ci.yml.

### Note
- The full enforcement remains the CI secrets-scan gate; this hook is a convenience/early filter.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a tracked file with a plaintext secret, When git push triggers the pre-push hook, Then the push is refused
- [ ] #2 Given the repository, When tools/check-git-hooks.py runs, Then the pre-push hook exists, is executable, runs the secret scanner, and the devcontainer wires core.hooksPath to .githooks
- [ ] #3 Given a fresh devcontainer, When postCreate has run, Then core.hooksPath points at the committed .githooks directory
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Delivered (local)
- .githooks/pre-push: runs tools/check-no-plaintext-secrets.py and blocks the push on findings (bypassable with --no-verify; CI stays the backstop).
- .devcontainer/postCreateCommand.sh: sets core.hooksPath=.githooks.
- tools/check-git-hooks.py: self-test + check (hook exists/executable/runs scanner; postCreate wires core.hooksPath); CI step added to the secrets-scan job.

### Verification
- check self-test + real check green; the hook runs and scans the tracked tree clean.
- Red-green: dropping the hook's exec bit makes the check fail; restoring it passes.

### Open
- DoD 'all CI green' confirmed by the operator after push.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->
