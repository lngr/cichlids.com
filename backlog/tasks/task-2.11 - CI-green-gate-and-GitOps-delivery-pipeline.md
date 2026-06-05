---
id: TASK-2.11
title: CI green gate and GitOps delivery pipeline
status: To Do
assignee: []
created_date: '2026-06-03 16:02'
updated_date: '2026-06-05 10:25'
labels:
  - infra
  - ci
  - gitops
  - pipeline
milestone: m-0
dependencies:
  - TASK-2.7
  - TASK-2.1
references:
  - .github/workflows/
parent_task_id: TASK-2
priority: high
ordinal: 13000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Wire the GitHub Actions pipeline that enforces the green gate and drives GitOps delivery
(ADR-0003 / ADR-0005 / ADR-0010).

### Green gate (required checks)
Extend `.github/workflows/` with the required checks, alongside the existing `adr-index` and
`story-test-binding` jobs — a failing required check blocks the merge:
- `toolchain`
- `infra-validate`, `infra-policy`
- `secrets-scan`
- `manifests`
- `backend`, `client-core`
- `e2e`

### Delivery mechanics
- **Image build:** build and push the container image to GHCR.
- **Staging:** open an image-tag bump pull request into the `staging` overlay, auto-merged; no
  automatic write-back beyond the PR.
- **Prod:** promotion is deliberate and **triggered by cutting a git tag**, wired to a **GitHub
  release** — *not* a promotion PR. Cutting the release promotes the released image to the `prod`
  overlay.
- **Infra:** run `tofu plan` as a PR check; `tofu apply` for the app tier after merge with the
  routine token.
- **Previews:** automate per-PR preview environment creation and teardown.

### Out of scope
- Branch protection on `main` is configured by the operator (documented in the runbook).

### Realises / verifying artifact
- Realises ADR-0005 (green gate, infra pipeline) and ADR-0010 (overlays; staging tag-bump via
  PR, prod via release tag).
- Verifying artifact: `.github/workflows/` (carries the relevant task id as back-reference).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a pull request, When CI runs, Then all required checks (adr-index, story-test-binding, toolchain, infra-validate, infra-policy, secrets-scan, manifests, backend, client-core, e2e) execute and a failure blocks merge
- [ ] #2 Given a merge to main, When the pipeline runs, Then the image is built/pushed and a tag-bump PR into the staging overlay is opened and auto-merged
- [ ] #3 Given a release to prod, When a git tag is cut and its GitHub release is published, Then the released image is promoted to the prod overlay with no promotion PR
- [ ] #4 Given infra/app changes, When merged, Then tofu plan ran as a PR check and tofu apply runs post-merge with the routine token
- [ ] #5 Given a pull request opened and later closed, When the pipeline runs, Then a preview environment is created and then torn down
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->
