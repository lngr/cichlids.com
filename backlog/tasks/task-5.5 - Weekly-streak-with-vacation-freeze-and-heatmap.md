---
id: TASK-5.5
title: Weekly streak with vacation freeze and heatmap
status: To Do
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-15 20:01'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 85000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A streak counts weeks with completed care, survives vacations through a freeze, shows a heatmap over 16 weeks and can be switched off in the settings.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who logged care in each of the last three weeks, When they open their streak, Then it shows three weeks and the heatmap marks those days
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Streak rule (decided by the project owner, 2026-10-15)
- A week (ISO week) counts when the member has at least one care entry in it (water change or measurement, any tank).
- Up to 2 missed weeks are bridged automatically by a freeze; every 4 kept weeks give back one freeze, at most 2 in reserve.
- The streak can be switched off in the settings.
<!-- SECTION:NOTES:END -->
