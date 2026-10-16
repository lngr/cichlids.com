---
id: TASK-5.17
title: Care heatmap over 16 weeks
status: To Do
assignee: []
created_date: '2026-10-16 09:22'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 97000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A calendar heatmap (contribution graph style) shows on which days the member cared for a tank over the last 16 weeks: water changes and measurements, independent of the streaks (decided by the project owner, 2026-10-16). Shown on the tank page and, summed over all tanks, on Today or Me; visibility follows the care stream (TASK-5.7).

### Reference
- TASK-5.5 notes, DRAFT-11 (target Today), PRODUCT-VISION epic 2.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who logged care on several days of the last 16 weeks, When they open the tank page, Then the heatmap marks exactly those days
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
