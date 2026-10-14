---
id: TASK-2.19
title: >-
  Automated dependency-update config (Renovate) for providers, Actions, and
  toolchain
status: In Progress
assignee: []
created_date: '2026-06-09 06:16'
updated_date: '2026-10-14 10:51'
labels:
  - infra
  - ci
  - maintenance
milestone: m-0
dependencies:
  - TASK-2.18
references:
  - renovate.json
parent_task_id: TASK-2
priority: medium
ordinal: 21000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Stop provider/toolchain pins from silently going stale (the root cause behind the deprecated hetznerdns provider and the ~> 1.49 hcloud pin): add a Renovate config that opens update PRs when a newer version is released.

### Scope
- renovate.json (or .github/renovate.json5): enable the native terraform manager (OpenTofu provider pins in infra/*/versions.tf + required_version) and the github-actions manager (.github/workflows/*).
- customManagers (regex) for the pinned toolchain binary versions in tools/ (tofu, conftest, opa, sops, age) so they are tracked too - this is exactly where drift went unnoticed.
- Sensible grouping/schedule; surface major bumps (e.g. aws 5->6, tofu minors) as separate PRs so breaking changes are reviewed deliberately.

### Constraints
- Config only; opening real PRs depends on the Renovate app/Action being enabled on the repo (operator step, out of band).
- Complements TASK-2.14 (one-time toolchain refresh) by keeping it current going forward.

### Realises
- ADR-0005 (CI/green-gate hygiene); supports the reproducible-toolchain intent of ADR-0001/TASK-2.1.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the Renovate config, When validated with renovate-config-validator, Then it passes with no errors
- [ ] #2 Given infra/foundation/versions.tf and infra/backup/versions.tf, When Renovate runs, Then the terraform manager detects the hcloud and aws provider pins as updatable dependencies
- [ ] #3 Given the pinned toolchain versions in tools/, When Renovate runs, Then a custom manager detects them so a new tofu/conftest/opa/sops/age release would raise a PR
- [ ] #4 Given .github/workflows, When Renovate runs, Then the github-actions manager detects the pinned action versions
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
- `renovate.json5` with the terraform manager (`infra/*/versions.tf`, `required_version`), the
  github-actions manager (SHA pins with version comments), npm/pnpm, NuGet, Dockerfile and
  Kubernetes image digests, kustomize, and custom regex managers for `tools/toolchain.versions`
  and the pinned versions in `tools/install-toolchain.sh` (tofu, conftest, opa, sops, age,
  kustomize, kubeconform, kind, helm, gitleaks, actionlint) and the pinned app tier versions (k3s,
  Argo CD, KSOPS).
- Grouping and schedule (weekly), majors as separate pull requests, no automerge.
- Validation with `renovate-config-validator` (pinned through npx) as a CI step; a local
  `renovate --platform=local` dry run shows the detected dependencies (AC #2 to #4).
<!-- SECTION:PLAN:END -->
