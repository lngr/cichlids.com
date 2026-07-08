---
id: TASK-3.6
title: ETL migrates comments and ratings
status: In Progress
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-08 22:49'
labels: []
dependencies:
  - TASK-3.5
parent_task_id: TASK-3
priority: high
ordinal: 26000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
All comments in batches via binary COPY; rows with empty note and a rating become pure ratings; comment votes from comments_rated; moderation trail preserved.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given migrated posts and tanks, When the comment ETL step runs twice, Then all non-filtered legacy comments exist exactly once, rows with an empty note and a rating become pure ratings, and per-target counts match the source
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
