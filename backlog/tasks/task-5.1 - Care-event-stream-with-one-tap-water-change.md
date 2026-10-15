---
id: TASK-5.1
title: Care event stream with one-tap water change
status: In Progress
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-15 12:27'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: high
ordinal: 81000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Care entries form an event stream per tank. A water change is logged with one tap on the tank page and from the + tab; the amount (liters, gallons or percent) can be added later. Every entry appears in the care view and the timeline of the tank.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member with a tank, When they tap Water change on the tank page, Then a water change entry with the current time exists in the care view of that tank and can be completed with an amount afterwards
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
