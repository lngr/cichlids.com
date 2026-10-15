---
id: TASK-4.4
title: Localized units with the input unit stored
status: In Progress
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 09:14'
labels: []
dependencies: []
parent_task_id: TASK-4
priority: medium
ordinal: 75000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Units follow the device country (metric or US) and can be changed separately from the language;
the API stores metric values plus the unit the member entered, so a 55 gallon tank always shows
as 55 gallons.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member in the US who creates a 55 gallon tank, When a member with metric units and later the creator open it, Then the metric member sees the liters and the creator sees exactly 55 gallons
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
