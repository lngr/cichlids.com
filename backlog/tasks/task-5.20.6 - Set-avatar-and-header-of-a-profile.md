---
id: TASK-5.20.6
title: Set avatar and header of a profile
status: To Do
assignee: []
created_date: '2026-10-16 14:17'
labels: []
dependencies:
  - TASK-5.20.4
parent_task_id: TASK-5.20
priority: low
ordinal: 107000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A member picks an avatar and a header for their profile from their media library or a fresh upload; both are media links with context profile, at most one each (ADR-0029). They are visible to everyone while the profile is active.

### Reference
- ADR-0029, TASK-5.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member with photos in the library, When they choose one as avatar, Then their profile, comments and stories show it
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
