---
id: TASK-3.27
title: Mobile app community archive screens
status: In Review
assignee: []
created_date: '2026-07-08 19:34'
updated_date: '2026-07-09 03:34'
labels: []
dependencies:
  - TASK-3.25
parent_task_id: TASK-3
priority: medium
ordinal: 47000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Archive section in the app: category list, thread list, thread detail in read-only presentation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the seeded local stack, When the community archive is opened in the app, Then categories, threads and posts render read-only, verified by a Maestro flow
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Read-only community screens (categories, thread list, thread detail with archive notice) shipped with the app shell.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
