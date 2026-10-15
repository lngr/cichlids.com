---
id: TASK-5.7
title: Visibility per care entry
status: To Do
assignee: []
created_date: '2026-10-15 12:26'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 87000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Each care entry has a visibility; private entries never leave the owner, public entries appear in the tank timeline for others.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a private and a public entry of a tank, When another member opens the tank, Then they see only the public entry
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
