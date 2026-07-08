---
id: TASK-3.8
title: ETL verification report
status: In Progress
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-08 23:53'
labels: []
dependencies:
  - TASK-3.2
  - TASK-3.3
  - TASK-3.4
  - TASK-3.5
  - TASK-3.6
  - TASK-3.7
parent_task_id: TASK-3
priority: high
ordinal: 28000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Final ETL stage comparing source and target counts per entity and per filter reason, listing every mismatch.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a completed ETL run, When the verification step runs, Then it reports source versus target counts per entity and per filter reason, lists every mismatch, and exits nonzero on unexpected differences
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
