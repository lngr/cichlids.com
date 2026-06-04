---
id: TASK-2.4
title: OpenTofu app tier and k3s plus Argo CD bootstrap
status: To Do
assignee: []
created_date: '2026-06-03 16:00'
updated_date: '2026-06-04 12:56'
labels:
  - infra
  - opentofu
  - k3s
  - argocd
milestone: m-0
dependencies:
  - TASK-2.2
references:
  - infra/app/
parent_task_id: TASK-2
priority: high
ordinal: 6000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Define the ephemeral (**app**) OpenTofu tier that routine automation may apply.

### Scope
- Install single-node **k3s** via cloud-init on the foundation node (the bundled Traefik
  disabled), and hand off to an **Argo CD** install so the cluster reconciles the rest from Git.
- This tier must reference **none** of the foundation tier's data resources (separate state,
  Layer 2 of ADR-0014).
- The CI infra pipeline runs `tofu plan` as a pull-request check and `tofu apply` after merge
  using the routine least-privilege token (ADR-0005).

### Realises
- ADR-0010 (provisioning split, k3s+Argo bootstrap, Traefik disabled), ADR-0014 (Layer 2
  state/credential separation).

### Verifying artifact
- `infra/app` + a check asserting the app tier resolves without referencing any foundation
  resource address.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given infra/app, When tofu fmt -check and tofu validate run, Then both pass
- [ ] #2 Given the app tier, When checked, Then it references no foundation resource address and holds no destroy-capable credential
- [ ] #3 Given a merge to main touching infra/app, When the infra pipeline runs, Then tofu plan ran as a PR check and tofu apply runs post-merge with the routine token
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
