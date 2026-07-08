---
id: TASK-3.10
title: Pictures read API with legacy parity
status: To Do
assignee: []
created_date: '2026-07-08 18:58'
labels: []
dependencies:
  - TASK-3.5
parent_task_id: TASK-3
priority: high
ordinal: 30000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
GET /pictures list with sort, type, user and paging semantics of the legacy API including visibility rules and rating sort; picture detail with variant URLs and view counting.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a seeded local stack, When GET pictures is called with sort, type, user and paging parameters, Then the response matches the legacy list semantics including visibility filtering
- [ ] #2 Given a picture slug, When the picture detail is fetched, Then the response contains the media variant URLs and the view counter increments
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
