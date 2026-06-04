---
id: TASK-2.3
title: Bootstrap runbook and credential tiers
status: To Do
assignee: []
created_date: '2026-06-03 16:00'
updated_date: '2026-06-04 12:56'
labels:
  - infra
  - docs
  - operator
milestone: m-0
dependencies:
  - TASK-2.4
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

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
