---
id: TASK-3.14
title: Comments and ratings API
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-09 02:33'
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Write endpoints for comments and ratings with legacy-parity split, set-based aggregate recompute, transactional outbox events, moderator delete with trail. Real smoke on live data; aggregate consistency check back to 0 after the picture-step upsert fix.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
