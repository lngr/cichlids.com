---
id: TASK-3.23
title: Seeded local stack end to end smoke
status: To Do
assignee: []
created_date: '2026-07-08 18:58'
labels: []
dependencies:
  - TASK-3.16
  - TASK-3.19
  - TASK-3.8
parent_task_id: TASK-3
priority: high
ordinal: 43000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Documented local bootstrap: stack up, migrate, ETL, media subset seed; one Maestro smoke flow over the whole seeded stack.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a clean checkout, When the documented local bootstrap runs, Then API, Keycloak, object store and app are usable end to end and the smoke Maestro flow passes
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
