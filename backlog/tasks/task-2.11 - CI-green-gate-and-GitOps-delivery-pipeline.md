---
id: TASK-2.11
title: CI green gate and GitOps delivery pipeline
status: To Do
assignee: []
created_date: '2026-06-03 16:02'
updated_date: '2026-06-03 16:03'
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
Wire the GitHub Actions pipeline that enforces the green gate and drives delivery (ADR-0003/0005/0010). Extend .github/workflows with the required checks (toolchain, infra-validate, infra-policy, secrets-scan, manifests, backend, client-core, e2e) alongside the existing adr-index and story-test-binding jobs. Add the delivery mechanics: build and push the container image to GHCR, then open an image-tag bump pull request into the target overlay (staging auto-merged, prod via a deliberate promotion PR) with no automatic write-back beyond the PR; run tofu plan as a PR check and tofu apply for the app tier after merge with the routine token; and automate per-PR preview creation and teardown. Branch protection on main is configured by the operator (documented in the runbook).

Realises ADR-0005 (green gate, infra pipeline) and ADR-0010 (image-tag bump via PR, overlays). Verifying artifact: .github/workflows/ (carries the relevant task id as back-reference).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a pull request, When CI runs, Then all required checks (adr-index, story-test-binding, toolchain, infra-validate, infra-policy, secrets-scan, manifests, backend, client-core, e2e) execute and a failure blocks merge
- [ ] #2 Given a merge to main, When the pipeline runs, Then the image is built/pushed and a tag-bump PR into the staging overlay is opened and auto-merged
- [ ] #3 Given promotion to prod, When triggered, Then it happens only via a deliberate promotion PR bumping the prod overlay tag
- [ ] #4 Given infra/app changes, When merged, Then tofu plan ran as a PR check and tofu apply runs post-merge with the routine token
- [ ] #5 Given a pull request opened and later closed, When the pipeline runs, Then a preview environment is created and then torn down
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
