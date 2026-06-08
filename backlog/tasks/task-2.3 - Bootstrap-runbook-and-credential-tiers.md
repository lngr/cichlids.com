---
id: TASK-2.3
title: Bootstrap runbook and credential tiers
status: In Progress
assignee:
  - claude
created_date: '2026-06-03 16:00'
updated_date: '2026-06-08 13:14'
labels:
  - infra
  - docs
  - operator
milestone: m-0
dependencies:
  - TASK-2.16
references:
  - infra/README.md
parent_task_id: TASK-2
priority: high
ordinal: 5000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Author the operator runbook that turns the OpenTofu code into a running platform, plus the
credential model.

### Operator steps
- Create the Hetzner Cloud account/project and a US-region node (Ashburn, ~CPX41, tunable).
- Mint a **routine least-privilege** token (no delete rights on protected/stateful resources)
  for CI, and a separate **full break-glass** token kept strictly out-of-band.
- Create a Hetzner DNS token and point cichlids.com at Hetzner nameservers.
- Create the Cloudflare account + R2 + S3 token (image serving, used later).
- Generate the SOPS age key (public key into `.sops.yaml`, private key out-of-band and as a
  cluster secret).
- Run `tofu init/plan/apply` for the foundation then the app tier.
- Set the GitHub Actions secrets; enable branch protection on main.

### Constraints
- The runbook lives in `infra/README.md` and contains no secrets.
- Documents the ADR-0014 invariant: the only destroy-capable credential exists solely on the
  operator host — never in the repo/CI/devcontainer.

### Realises
- ADR-0005 (branch protection, infra pipeline), ADR-0009/0010 (providers), ADR-0014 (credential
  tiers, break-glass).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given infra/README.md, When the operator follows it end to end, Then they can create accounts/tokens and apply the foundation and app tiers without further instructions
- [ ] #2 Given the runbook, When reviewed, Then it states the break-glass credential is out-of-band only and lists every required GitHub Actions secret and the branch-protection setup
- [ ] #3 Given the repository, When scanned, Then the runbook contains no secrets or tokens
<!-- AC:END -->



## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Goal
Author the operator runbook (infra/README.md) and the test-first secret guard.

### Steps
- Write infra/README.md: accounts, the single Read & Write token reality (break-glass = console/owner access), Hetzner DNS, the out-of-band state-bucket bootstrap, SOPS age key, foundation + app tofu apply with expected results, GitHub Actions secrets, branch protection — no secrets.
- Add tools/check-no-plaintext-secrets.py (self-test + tracked-file scan; workspace-legacy and lock files excluded) and wire the CI job secrets-scan.
- Carry the task-2.3 back-tag in infra/README.md for the story-test binding.

### Verification
- Red-green on the secret scanner; all local gates green.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Delivered (local)
- infra/README.md: bootstrap runbook + credential model (ADR-0009/0010/0013/0014/0016). Numbered operator steps, each with an expected result. No secrets (env-var supplied / SOPS-encrypted). Back-tag task-2.3.
- tools/check-no-plaintext-secrets.py: offline, no-deps secret detector (private keys, AWS access keys, age secret keys, credential=value heuristic with placeholder allowlist). Scope = git-tracked files minus workspace-legacy and lock files. CI job secrets-scan (self-test then scan).
- Latent green-gate fix: check-story-test-binding.mjs matched the upper-case task id against the lower-case back-tag case-sensitively; made it case-insensitive (+ covering self-test).

### Local gates (green)
- secrets-scan, story-test-binding (self-test + real), adr-index, foundation guardrails, tofu fmt.

### Open / handoff
- App-tier code (infra/app) not built yet; runbook documents its apply with the precondition, so only the foundation apply is executable today.
- Dependency cleanup: the stale TASK-2.4 link cannot be removed via the CLI (no clear option) and hand-editing task files is disallowed; left as harmless board metadata.
- DoD 'all CI green' is confirmed by the operator after push/merge.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 At least one Given-When-Then acceptance criterion is specified
- [x] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->
