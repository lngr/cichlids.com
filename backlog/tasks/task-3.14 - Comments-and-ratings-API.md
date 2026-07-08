---
id: TASK-3.14
title: Comments and ratings API
status: To Do
assignee: []
created_date: '2026-07-08 18:58'
labels: []
dependencies:
  - TASK-3.6
  - TASK-3.13
parent_task_id: TASK-3
priority: high
ordinal: 34000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Posting comments with optional star rating on pictures and tanks, comment lists, moderator delete with reason and moderation trail, rating aggregates.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an authenticated user, When a comment with an optional star rating is posted on a picture or tank, Then it appears in the target comment list and the rating aggregate of the target updates
- [ ] #2 Given a moderator, When a comment is deleted with a reason, Then it disappears from public lists but stays stored with its moderation trail
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
