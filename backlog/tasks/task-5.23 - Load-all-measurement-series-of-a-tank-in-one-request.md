---
id: TASK-5.23
title: Load all measurement series of a tank in one request
status: To Do
assignee: []
created_date: '2026-10-16 15:48'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: low
ordinal: 109000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The measurement charts of a tank load the series of all parameters with one request; each parameter has its own request (TASK-5.2), which costs ten round trips on a phone connection. Visibility and only-me rules of the care stream apply as for the single series.

### Reference
- TASK-5.2, TASK-5.7, TASK-5.14.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a tank with measurements of several parameters, When the member opens the care view, Then the charts appear after one series request and show the same values as before
- [ ] #2 Given a visitor who may see the care stream, When they open the charts, Then only-me measurements are left out as in the single series
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
