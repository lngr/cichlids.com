---
id: TASK-3.30
title: >-
  Run the ETL only against an empty target and reset dev and staging before each
  run
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: high
ordinal: 52000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R3, R18, R38.

The ETL writes only into an empty target database and aborts before its first write otherwise. Dev and staging get a reset that empties the database and the Keycloak realm users and rebuilds the schema, so every run starts from the same state and rehearses the go-live. The reset refuses to run in Production. Media objects stay in the object store across resets. The dry-run mode is removed. bootstrap.sh fails clearly when Postgres does not come up and imports the realm fresh after a reset.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a target database that holds any row, When the operator runs the full ETL, Then it aborts before writing and names the reason
- [ ] #2 Given the dev stack, When the operator runs the reset and then the full ETL and the account import, Then both complete and the verify step reports no mismatch
- [ ] #3 Given the Production environment setting, When the operator invokes the reset, Then it refuses to run
- [ ] #4 Given the ETL command line, When the operator passes --dry-run, Then the option is rejected as unknown
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
