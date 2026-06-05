---
id: TASK-2.1
title: Reproducible developer and CI toolchain
status: In Progress
assignee:
  - claude
created_date: '2026-06-03 16:00'
updated_date: '2026-06-05 10:25'
labels:
  - infra
  - tooling
milestone: m-0
dependencies: []
references:
  - tools/verify-toolchain.sh
parent_task_id: TASK-2
priority: high
ordinal: 3000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Provide the full toolchain to build, validate and test the platform reproducibly in the
devcontainer and in CI, so every later task runs against pinned, known-good tools.

### Tools
- OpenTofu, kubectl, helm, kustomize
- sops, age, conftest
- .NET 10 SDK
- JDK + Android SDK/cmdline-tools + a KVM-accelerated emulator, the Maestro CLI
- kind / k3d

### Approach
- Installed via the devcontainer image/features (versions pinned), exposed on PATH.
- A verification script asserts each tool is present at the expected version and is the single
  source of truth used by the CI `toolchain` job.

### Realises
- Execution-environment needs of ADR-0004/0006 (Maestro + Android emulator) and the IaC/GitOps
  tooling of ADR-0010/0014.

### Verifying artifact
- `tools/verify-toolchain.sh` (carries the task id as back-reference for the story-test-binding
  gate).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Given the devcontainer, When the core tools are installed via tools/install-toolchain.sh, Then tools/verify-toolchain.sh core exits 0 with every core tool (tofu, kubectl, helm, kustomize, sops, age, conftest, kind, kubeconform, node) at or above its pinned floor
- [ ] #2 Given CI, When the toolchain job runs, Then it installs the pinned core toolchain and verify-toolchain.sh core passes as a required check
- [x] #3 Given a missing or below-floor tool, When verify-toolchain.sh runs, Then it exits non-zero and names the offending tool
- [ ] #4 Given the backend and e2e toolchains, When their CI jobs are added with the smoke story, Then those jobs verify scopes backend and e2e respectively (heavy .NET/Android/Maestro installs live with their consuming story)
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach (test-first)
1. tools/verify-toolchain.sh (scopes core|backend|e2e|all, version floors from tools/toolchain.versions) — written and run red.
2. tools/install-toolchain.sh installs the pinned core single-binary tools idempotently into /usr/local/bin.
3. Wire it into .devcontainer/postCreateCommand.sh and a CI "toolchain" job.

### Notes
- Backend (.NET) and e2e (JDK/Android/Maestro) installers land with the smoke story that consumes them; verify-toolchain.sh already supports those scopes.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Verified locally
- Core toolchain green: tofu 1.9.1, kubectl 1.31.4, helm 3.16.4, kustomize 5.5.0, sops 3.9.3, age 1.2.1, conftest 0.56.0, kind 0.25.0, kubeconform 0.6.7, node 24.16.0.
- Installer idempotent on re-run; scripts pass bash -n.

### Handoff
- Not committed (operator owns git).
- DoD #2/#3/#5 (CI toolchain job + full CI green) verified by the operator after push.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 At least one Given-When-Then acceptance criterion is specified
- [x] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->
