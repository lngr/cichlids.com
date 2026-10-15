---
id: TASK-5.2
title: Measurements as separate events with value graphs
status: In Progress
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-15 19:08'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: high
ordinal: 82000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Measurements are separate care events (temperature, pH, KH, GH, NO2, NO3, NH4, PO4, conductivity or TDS), entered progressively with units of TASK-4.4 and their origin (test kit, meter, probe); every parameter has a graph over time.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who logs pH and nitrate on three days, When they open the care view, Then they see the values per parameter as a graph over time in their units
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
