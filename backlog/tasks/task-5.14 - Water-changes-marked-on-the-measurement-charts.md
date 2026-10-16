---
id: TASK-5.14
title: Water changes marked on the measurement charts
status: In Progress
assignee: []
created_date: '2026-10-16 06:28'
updated_date: '2026-10-16 06:29'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 94000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal (idea of the project owner, 2026-10-16)
The measurement charts of TASK-5.2 show the water changes of the tank as markers on the same time axis, so a member sees how a value such as nitrate rises between water changes and drops after each one (before and after). The chart works with whatever the member measures; no extra measurements are required. Tapping a marker opens the water change entry.

### Reference
- TASK-5.2 (charts and the follow-up to load all series in one request), TASK-5.1 and TASK-5.13 (water change entries).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a tank with nitrate measurements and water changes in between, When the member opens the nitrate chart, Then each water change appears as a marker on the time axis and the values before and after it are visible
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
