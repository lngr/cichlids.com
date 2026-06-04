---
id: TASK-2.9
title: CloudNativePG with object-store PITR backup
status: To Do
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-06-04 12:57'
labels:
  - infra
  - postgres
  - backup
  - gitops
milestone: m-0
dependencies:
  - TASK-2.7
references:
  - apps/postgres/
parent_task_id: TASK-2
priority: high
ordinal: 11000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Run PostgreSQL in-cluster via the **CloudNativePG** operator and make backup binding from day
one (ADR-0013).

### Scope
- Define the operator and a `Cluster` CR, with **Barman Cloud** object-store backups writing
  base backups and continuously archived WAL to the backup bucket (RPO≤5min).
- Tiered retention: a 14-day continuous PITR window plus monthly archival snapshots retained
  12 months.
- Staging and preview get their own ephemeral databases seeded with synthetic data (never
  production PII).
- The Cluster, its PVCs and namespace carry `Prune=false` and finalizers (ADR-0014 Layer 1).

### Realises
- ADR-0010 (CloudNativePG), ADR-0013 (continuous archiving, PITR, retention, RPO).

### Verifying artifact
- `apps/postgres` rendering + a policy check that backup, retention and `Prune=false` are present.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given apps/postgres, When kustomize build runs, Then it renders and passes schema validation
- [ ] #2 Given the Cluster CR, When inspected, Then it configures Barman Cloud object-store backup with continuous WAL archiving, a 14-day PITR window and 12 monthly snapshots
- [ ] #3 Given the stateful resources, When checked, Then the Cluster, PVCs and namespace set Prune=false and finalizers
- [ ] #4 Given staging/preview, When provisioned, Then their databases use synthetic seed data and never production PII
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
