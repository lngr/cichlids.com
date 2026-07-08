---
id: TASK-3.5
title: ETL migrates pictures to posts and media
status: In Progress
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-08 20:29'
labels: []
dependencies:
  - TASK-3.3
  - TASK-3.4
parent_task_id: TASK-3
priority: high
ordinal: 25000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Pictures to posts plus media items: type mapping from legacy pid, species links, counters, slugs, moderation trail; tank image CSV lists resolved into ordered attachments.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given migrated profiles and tanks, When the picture ETL step runs twice, Then every non-filtered legacy picture exists exactly once as a post with media item including type, species links, counters and slugs, and tank image lists are resolved into ordered attachments
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
