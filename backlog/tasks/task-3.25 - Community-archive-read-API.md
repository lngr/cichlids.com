---
id: TASK-3.25
title: Community archive read API
status: In Review
assignee: []
created_date: '2026-07-08 19:34'
updated_date: '2026-07-09 02:55'
labels: []
dependencies:
  - TASK-3.24
parent_task_id: TASK-3
priority: high
ordinal: 45000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Thread listing per forum category, thread detail with posts, archive presentation flags; member emails are never exposed.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given migrated discussions, When threads are listed by category and a thread with its posts is fetched, Then archived content is returned with authorship display names and without any email addresses
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Categories, thread list with query filter, thread detail with guest/placeholder/member authorship and attachments; no email addresses anywhere (tested). Real smoke: category counts sum to 14302 threads.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
