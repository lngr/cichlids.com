---
id: TASK-3.12
title: Species catalog read API
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-09 01:25'
labels: []
dependencies:
  - TASK-3.2
parent_task_id: TASK-3
priority: medium
ordinal: 32000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Species list and detail with taxonomy, care ranges and enums.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the migrated species catalog, When species are listed and fetched by id, Then taxonomy, care ranges and enum values are returned
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Species list with query filter plus detail with care data, common names and links. Real-stack smoke verified.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
