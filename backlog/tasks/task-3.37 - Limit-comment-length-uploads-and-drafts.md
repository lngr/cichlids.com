---
id: TASK-3.37
title: 'Limit comment length, uploads and drafts'
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: medium
ordinal: 61000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R11, R13.

Comments have at most 10000 characters and deletion reasons at most 500; JSON requests are capped at about 64 KB; the app shows a character counter. A member may upload about 30 photos per hour (configurable) and hold at most 50 open drafts; drafts untouched for 30 days are deleted with their files. Reaching a limit shows a clear message in the app.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a comment of 10001 characters, When the member submits it, Then the API refuses with a clear message
- [ ] #2 Given a member with 50 open drafts, When the member uploads another photo, Then the app shows the draft limit message
- [ ] #3 Given a draft untouched for 31 days, When the cleanup runs, Then the draft and its objects are gone
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
