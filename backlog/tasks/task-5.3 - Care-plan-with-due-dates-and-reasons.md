---
id: TASK-5.3
title: Care plan with due dates and reasons
status: To Do
assignee: []
created_date: '2026-10-15 12:26'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: high
ordinal: 83000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Each tank has a care plan with a rhythm per care type and a reason; the plan derives the due date from the last entry. The rhythm follows from volume and stocking as soon as species data exist (slice 4); until then a default applies.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a tank with a water change plan and a last water change, When the rhythm has passed, Then the tank shows the water change as due with the reason of the plan
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
