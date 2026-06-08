---
id: TASK-2.16
title: Restructure infrastructure into three Hetzner projects
status: To Do
assignee: []
created_date: '2026-06-08 13:14'
updated_date: '2026-06-08 13:14'
labels:
  - infra
milestone: m-0
dependencies:
  - TASK-2.13
parent_task_id: TASK-2
ordinal: 18000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Implement the capability-based blast-radius boundary from ADR-0017: split the infrastructure
across three Hetzner projects so the routine CI token cannot reach the irreplaceable data.

### Scope
- Three projects: `cichlids` (all live/rebuildable infra for every env), `cichlids-backup`
  (immutable DB + media-master backups, Object Lock Compliance), `cichlids-tfstate` (state
  bucket only).
- Separate hcloud providers/tokens and state keys per project; CI holds only the `cichlids`
  R&W token + S3 keys, and no Cloud token for backup/tfstate.
- Rework the current single-project foundation (node/volume/buckets/DNS in one project) and the
  bootstrap runbook accordingly (three projects, split credential flow).

### Realises
- ADR-0017 (project separation), refining ADR-0014 Layer 2; consistent with ADR-0016 (state).

### Note
- Reworks the single-project foundation/runbook delivered earlier; the foundation apply is
  gated on this. No implementation in this task's creation — planning/coding happen when picked up.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the OpenTofu configuration, When applied, Then live infrastructure is in the cichlids project while backups and state are in separate cichlids-backup and cichlids-tfstate projects
- [ ] #2 Given CI, When it runs OpenTofu, Then it holds only the cichlids project Read & Write token plus object-storage keys, and no Cloud token for the backup or state projects
- [ ] #3 Given the bootstrap runbook, When the operator follows it, Then the three projects and their credentials are created with the correct out-of-band vs in-CI split
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->
