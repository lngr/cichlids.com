---
id: TASK-2.11
title: CI green gate and GitOps delivery pipeline
status: In Review
assignee: []
created_date: '2026-06-03 16:02'
updated_date: '2026-10-14 10:27'
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
  - tools/delivery.py
  - docs/runbooks/ci-delivery.md
  - .github/workflows/images.yml
  - .github/workflows/release.yml
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
### Implemented
- `ci.yml`: the required checks plus a summary job `required` (no permissions) that fails when a
  required job failed, was cancelled or skipped; actionlint with ShellCheck as a check; every job
  `contents: read`; all actions pinned by commit SHA with the version as comment.
- `images.yml`: on merge to main build API, migration and web images, push to
  `ghcr.io/lngr/cichlids-{api,migration,web}` with the commit SHA tag (`packages: write` only in
  the build job); a staging job opens a bump pull request with the digests in the staging image
  overrides, dispatches CI for it and enables squash auto-merge. Preview images for
  same-repository pull requests admitted by the preview workflow, tagged with the head SHA the
  preview ApplicationSet uses.
- `release.yml`: a published release `v*` whose commit is on main promotes the digests of that
  commit (from GHCR or the committed staging snapshot) with one commit to the prod overlays.
- `tools/delivery.py` with 13 tests (bump, promotion, preflight); deployment preflight passes real
  digests and refuses placeholders.
- Runbook `docs/runbooks/ci-delivery.md`: repository settings, GHCR package visibility, the
  ruleset bypass for the release commit or a GitHub App token as alternative.

### Verified by the agent (Codex)
- Tests red then green; actionlint clean; manifests 285 valid; secret guard; policies 73 tests;
  gitleaks over tracked files and history clean; story binding.
- Not verified: runs on GitHub, image publication, auto-merge, release promotion, preview
  reconciliation. The web image carries the staging addresses; prod stays disabled.

### Operator checks
- Repository settings from the runbook, a merge to main builds the images and the staging bump
  pull request merges itself (AC #2); a release `v*` promotes to prod (AC #3); a pull request with
  label `preview` gets and loses its preview (AC #5); `required` blocks a red pull request (AC #1,
  with TASK-2.12).
<!-- SECTION:NOTES:END -->
