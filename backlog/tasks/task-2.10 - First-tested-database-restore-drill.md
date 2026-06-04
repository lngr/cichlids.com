---
id: TASK-2.10
title: First tested database restore drill
status: To Do
assignee: []
created_date: '2026-06-03 16:02'
updated_date: '2026-06-04 12:56'
labels:
  - infra
  - backup
  - restore
  - ci
milestone: m-0
dependencies:
  - TASK-2.9
references:
  - tools/restore-drill/
parent_task_id: TASK-2
priority: high
ordinal: 12000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Prove the recovery path, not just the backup (ADR-0013): a backup that has never been restored
is treated as broken.

### Scope
- An automated restore drill that, against an ephemeral CloudNativePG instance (local kind +
  MinIO is acceptable for the agent-side proof; the cluster's real backup bucket for the
  operator), seeds synthetic data, takes a backup, restores it into a fresh throwaway target and
  asserts the restored row counts match.
- Runs as a scheduled/manual CI job and tears the target down afterwards.

### Constraints
- The production restore-test environment that restores real (transient) PII is documented as
  out-of-band with no agent/CI access; this drill uses synthetic data only.

### Realises
- ADR-0013 (tested restores, RTO≤4h, restore-test environment).

### Verifying artifact
- `tools/restore-drill/` + CI job `restore-drill`.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given synthetic seed data and a fresh backup, When the restore drill restores into a throwaway target, Then the restored row counts equal the source and the drill exits 0
- [ ] #2 Given a backup that cannot be restored, When the drill runs, Then it fails and reports the discrepancy
- [ ] #3 Given the drill completes, When it finishes, Then the throwaway restore target is torn down
- [ ] #4 Given the production restore-test environment, When documented, Then it is described as out-of-band with no agent/CI access and handling only transient real PII
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
