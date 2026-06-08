---
id: TASK-2.14
title: Refresh pinned core toolchain to current stable versions
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
  - tools/toolchain.versions
parent_task_id: TASK-2
ordinal: 16000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Pin the core toolchain to current stable releases instead of the initial outdated ones, so the
platform starts on supported versions.

### Scope
- install-toolchain.sh pins: tofu 1.12.1, kubectl 1.36.1, helm 3.21.0 (latest 3.x; Helm 4 major deferred for Argo CD/chart compatibility), kustomize 5.8.1, sops 3.13.1, age 1.3.1, conftest 0.68.2, kind 0.32.0, kubeconform 0.8.0.
- toolchain.versions floors raised to match (patch-flexible major.minor.0); NODE_MIN 24.0.0. TOFU_MIN stays 1.10.0 as the feature floor (use_lockfile).
- Fix a false-positive presence check: sops --version prints an upstream 'newer available' notice whose version string falsely satisfied the substring check (and needs network); install/verify now pass --disable-version-check.

### Affected artifacts
- tools/install-toolchain.sh, tools/toolchain.versions, tools/verify-toolchain.sh.

### Out of scope
- backend/e2e floors (.NET, Java, Android, Maestro) — revisited when those jobs are built.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given tools/install-toolchain.sh, When it runs, Then it installs the current pinned stable versions of every core tool
- [ ] #2 Given the installed toolchain, When tools/verify-toolchain.sh core runs, Then every core tool meets its floor in tools/toolchain.versions
- [ ] #3 Given sops with an available upstream update, When the presence/verify checks run, Then they use --disable-version-check and report the actually-installed version
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Delivered (local)
- install-toolchain.sh pins bumped to current stable (tofu 1.12.1, kubectl 1.36.1, helm 3.21.0, kustomize 5.8.1, sops 3.13.1, age 1.3.1, conftest 0.68.2, kind 0.32.0, kubeconform 0.8.0).
- toolchain.versions floors raised (patch-flexible major.minor.0; NODE_MIN 24.0.0); TOFU_MIN kept at 1.10.0 as the feature floor.
- sops presence/verify fixed with --disable-version-check (the 'newer available' notice falsely matched and needs network).

### Verification
- bash tools/install-toolchain.sh then ./tools/verify-toolchain.sh core -> all OK (node 24.16.0 present).
- Red-green: before the sops fix, verify reported sops 3.9.3 < floor (red); after, 3.13.1 (green).

### Open
- DoD 'all CI green' confirmed by the operator after push.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->
