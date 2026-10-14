---
id: TASK-2.11
title: CI green gate and GitOps delivery pipeline
status: In Progress
assignee: []
created_date: '2026-06-03 16:02'
updated_date: '2026-10-14 10:15'
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

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach (test-first)
- Red first: tests for the delivery helpers (digest bump of an overlay, release promotion, the
  required-check list) and a workflow lint step (actionlint, pinned) before the workflows exist.

### Green gate
- `ci.yml` exposes exactly the required check names (adr-index, story-test-binding, toolchain,
  infra-validate, infra-policy, secrets-scan, manifests, backend, client-core, e2e); a summary job
  `required` depends on all of them, so branch protection (TASK-2.12) needs one check name.
  `infra-policy` runs the conftest policies on fixtures and the app tier plan.

### Images (merge to main)
- Workflow `images.yml`: build the API, migration and web images of TASK-2.31 with Buildx, push to
  GHCR (`ghcr.io/lngr/cichlids-*`) with the commit SHA tag, record the digests, sign nothing yet;
  `permissions: packages: write, contents: read` only in that job.
- Staging bump: a second job opens a pull request that sets the new digests in the staging
  overlays (kustomize image override) and enables auto-merge; the pull request runs the normal
  checks. Uses `GITHUB_TOKEN` with `contents: write, pull-requests: write`; the operator allows
  Actions to create pull requests and enables auto-merge in the repository settings.

### Production (release)
- Workflow `release.yml` on a published GitHub release of a tag `v*`: resolves the digests of the
  release commit from GHCR and commits them to the prod overlays on main (no promotion pull
  request), guarded so only a release from main promotes.

### Infra and previews
- The app tier plan and apply stay in `infra-app.yml` (TASK-2.4); AC #4 is covered there.
- Previews: the pull request images of same-repository pull requests with the label `preview`
  are built with the pull request SHA tag; the preview ApplicationSet of TASK-2.7 uses them;
  teardown follows the pull request close (TASK-2.7). Fork pull requests never build or deploy.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The backend, client-core and e2e checks listed above are delivered by TASK-2.28; this task wires them as required checks.
<!-- SECTION:NOTES:END -->
