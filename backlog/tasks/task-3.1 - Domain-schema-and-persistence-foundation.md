---
id: TASK-3.1
title: Domain schema and persistence foundation
status: To Do
assignee: []
created_date: '2026-07-08 18:58'
labels: []
dependencies: []
parent_task_id: TASK-3
priority: high
ordinal: 21000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
EF Core 10 model and initial migration for the accepted domain schema, with a unique legacy_id index on every migrated aggregate.

### Notes
- Blocked until the schema ADR is accepted
- Integration test applies migrations against Postgres via Testcontainers
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an empty Postgres database from the local stack, When the EF Core migrations are applied, Then all core tables exist with unique legacy_id indexes and the schema matches the accepted schema ADR
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
