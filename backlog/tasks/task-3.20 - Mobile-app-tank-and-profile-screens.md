---
id: TASK-3.20
title: Mobile app tank and profile screens
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-09 03:34'
labels: []
dependencies:
  - TASK-3.19
  - TASK-3.11
parent_task_id: TASK-3
priority: medium
ordinal: 40000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Tank detail with image sections and public profile screen.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the seeded local stack, When a tank and a user profile are opened in the app, Then tank data with image sections and public profile data render, verified by a Maestro flow
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Tank list and detail with datasheet and section galleries plus profile screen shipped with the app shell; Maestro flow checked in, execution blocked as on 3.19.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
