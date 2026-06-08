---
id: TASK-2.13
title: Remote OpenTofu state backend (Hetzner Object Storage)
status: In Progress
assignee:
  - claude
created_date: '2026-06-05 13:37'
updated_date: '2026-06-05 20:02'
labels:
  - infra
milestone: m-0
dependencies: []
references:
  - infra/foundation/backend.tf
parent_task_id: TASK-2
ordinal: 15000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Give both OpenTofu tiers a durable, lockable, encrypted remote state backend so the stateful
tier's management handle cannot be lost with a single machine (ADR-0016). Closes the state-
durability gap left open by ADR-0010/ADR-0014.

### Scope
- Add a remote `backend "s3"` block (Hetzner Object Storage, native `use_lockfile` locking,
  OpenTofu >= 1.10) and a native `encryption` block (passphrase out-of-band via
  TF_VAR_state_encryption_passphrase) to infra/foundation; the app tier adopts the same pattern
  when it is built.
- Bump the pinned toolchain floor to OpenTofu >= 1.10 (use_lockfile).
- Document the one-time, out-of-band state-bucket bootstrap (dedicated cichlids-tfstate,
  versioning on, Object Lock OFF) as the first operator step in infra/README.md.

### Mechanics
- The state bucket is NOT managed by either tier (chicken-and-egg with media-master/backup).
- No Object Lock on the state bucket: an Object-Lock retention would make the lock object
  undeletable and cause stuck locks.

### Affected artifacts
- infra/foundation/backend.tf (new), infra/foundation/versions.tf, tools/toolchain.versions,
  tools/install-toolchain.sh, tools/check-remote-state-backend.py (new), .github/workflows/ci.yml,
  infra/README.md.

### Realises
- ADR-0016 (remote state backend), ADR-0010/ADR-0014 (state separation/durability).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the OpenTofu tiers, When tools/check-remote-state-backend.py runs, Then every present tier declares a remote s3 backend (no implicit local state) and the state bucket is not a managed media-master/backup bucket
- [ ] #2 Given infra/foundation with the pinned toolchain (OpenTofu >= 1.10), When tofu validate runs, Then the backend and native state-encryption blocks are present and the configuration is valid
- [ ] #3 Given infra/README.md, When the operator follows it, Then the dedicated state bucket is created out-of-band (versioning on, Object Lock off) before tofu init and the encryption passphrase is supplied out-of-band
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Delivered (local)
- ADR-0016 'Remote OpenTofu State Backend on Hetzner Object Storage' (Accepted); ADR index regenerated.
- infra/foundation/backend.tf: backend "s3" on Hetzner Object Storage (native use_lockfile locking) + native state-encryption block (passphrase via TF_VAR_state_encryption_passphrase, out-of-band; empty default keeps validate runnable). Back-tag task-2.13.
- versions.tf required_version >= 1.10.0; toolchain floor TOFU_MIN=1.10.0 (use_lockfile minimum), install pin TOFU_VERSION=1.12.1 (latest stable).
- tools/check-remote-state-backend.py + CI step (infra-validate): asserts each present tier has a remote s3 backend and the state bucket != managed media-master/backup buckets.
- infra/README.md reworked: out-of-band state-bucket bootstrap (cichlids-tfstate, versioning on, Object Lock OFF), tofu init with backend (AWS_* env for state creds) + encryption passphrase, corrected Hetzner token reality.

### Verification (OpenTofu 1.12.1)
- tofu fmt -check; init -backend=false + validate (foundation); guardrails; backend check (self-test + red/green); secret scan — all green.

### Open
- App-tier (infra/app) gets the same backend block when built (its own task).
- DoD 'all CI green' confirmed by the operator after push.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 At least one Given-When-Then acceptance criterion is specified
- [x] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->
