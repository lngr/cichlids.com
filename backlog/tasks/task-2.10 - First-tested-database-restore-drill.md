---
id: TASK-2.10
title: First tested database restore drill
status: In Review
assignee: []
created_date: '2026-06-03 16:02'
updated_date: '2026-10-14 10:51'
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
  - tools/restore-drill/README.md
  - .github/workflows/restore-drill.yml
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
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach (test-first)
- `tools/restore-drill/` with a drill script that creates a kind cluster, an S3-compatible store
  (MinIO pinned by digest, or RustFS as in the local stack), installs the pinned CloudNativePG
  operator and Barman Cloud plugin of TASK-2.9, creates a source cluster with synthetic data,
  takes a base backup plus WAL, restores into a fresh target cluster from the object store, and
  compares row counts per table; exits non-zero with the differing tables.
- Red first: a negative mode that corrupts or removes the backup and must fail (AC #2), and a
  check that the target and the kind cluster are gone afterwards (AC #3, trap on exit).
- Reuse the manifests of `apps/postgres` (base) where possible, so the drill exercises the real
  configuration.

### CI
- Workflow job `restore-drill` (scheduled monthly plus manual dispatch), no secrets, synthetic data
  only.

### Docs
- `tools/restore-drill/README.md`: running the drill; the production restore-test environment is
  out-of-band, operated by the operator only, no agent or CI access, handles real PII only
  transiently (AC #4); RTO measurement.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- `tools/restore-drill/`: a kind cluster with a local S3 store, the pinned cert-manager,
  CloudNativePG v1.30.1 and the Barman Cloud plugin v0.15.1 with the `apps/postgres` base;
  synthetic data in three tables, base backup, further writes, WAL switch, recovery into a fresh
  target cluster, comparison of row counts and checksums per table; `--break-backup` removes the
  base archive and must fail; a trap removes target, kind cluster and networks; the restore
  duration is printed.
- Workflow `restore-drill.yml`: monthly and on dispatch, no secrets, runs the drill and the
  negative mode.
- README: the production restore-test environment is out of band, run by the operator only, no
  agent or CI access, real personal data only transiently.

### Verified by the agent (Codex) and the main session
- Red: the drill failed without the recovery step. Green: measurements 9231, observations 7000,
  tanks 2500 rows with equal checksums, restore 52.24 s. Negative: exit 1 with
  "Missing file source/base/... data.*". No kind cluster or drill container left.
- Not verified: the run on GitHub; the operator restore from the real backup bucket.
<!-- SECTION:NOTES:END -->
