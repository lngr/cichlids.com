---
id: TASK-3.36
title: One rating per member and target and no self-rating
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: medium
ordinal: 60000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R9.

A member has exactly one rating per target; a new rating replaces the previous one, enforced by a unique index. Members cannot rate their own content; the app offers no stars there. The ETL keeps the latest legacy rating per member and target, drops legacy self-ratings (the comment text stays) and reports both counts.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who rated a picture, When the member rates it again, Then the picture has one rating by that member with the new value
- [ ] #2 Given a member viewing their own picture, When the member tries to rate it through the API, Then the API refuses
- [ ] #3 Given the import report, When the operator reads it, Then it lists the collapsed legacy ratings and the dropped self-ratings
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
