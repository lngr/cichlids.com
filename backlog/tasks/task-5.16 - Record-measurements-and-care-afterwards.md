---
id: TASK-5.16
title: Record measurements and care afterwards
status: To Do
assignee: []
created_date: '2026-10-16 07:10'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: high
ordinal: 96000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal (requested by the project owner, 2026-10-16)
Members often note water values and enter them a day or two later. The measurement form and the quick log offer the time of the measurement or care entry: now by default, or a chosen earlier date and time (not in the future). The API already accepts past times (TASK-5.1); the app does not offer them yet.

### Reference
- TASK-5.1, TASK-5.2 (measurement form), TASK-5.3 (due state follows the occurred time), TASK-5.14 (chart markers).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who measured nitrate two days ago, When they enter the value with that date in the measurement form, Then the entry and the chart show the measurement at that date
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
