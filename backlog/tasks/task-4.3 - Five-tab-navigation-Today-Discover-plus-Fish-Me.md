---
id: TASK-4.3
title: Five-tab navigation Today Discover plus Fish Me
status: In Progress
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 09:14'
labels: []
dependencies: []
parent_task_id: TASK-4
priority: high
ordinal: 74000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The app has the tabs Today, Discover, + (create), Fish and Me. The gallery and the forum archive
move into Discover, the species list into Fish; the navigation between views is owned by XState
machines (ADR-0007).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a signed-in member, When they open the app, Then the five tabs are shown, the gallery and the forum archive are reachable from Discover and the species list from Fish
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
