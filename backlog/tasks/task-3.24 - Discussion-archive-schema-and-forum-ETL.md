---
id: TASK-3.24
title: Discussion archive schema and forum ETL
status: In Review
assignee: []
created_date: '2026-07-08 19:34'
updated_date: '2026-07-08 23:53'
labels: []
dependencies:
  - TASK-3.1
  - TASK-3.3
parent_task_id: TASK-3
priority: high
ordinal: 44000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Discussion thread and post entities per ADR-0021 plus the forum ETL stage: visible phorum messages, authors matched to profiles by email, other registered authors as unlisted placeholders, guests as display names, attachments exported from the database into the object store as media items. Private messages, credentials and stored emails are excluded.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the restored phorum database and migrated profiles, When the forum ETL runs twice, Then all visible forum threads and posts exist exactly once with authorship mapped per ADR-0021, attachments stored as media items, and no private messages or email addresses migrated
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Real double run: 14302 threads (cichlids 7859, african 6194, market_place 249), 65348 posts (32478 guest, 32870 attributed, 690 posting authors matched by email, 1563 forum placeholder profiles), 1396 attachments decoded from the database into the object store (239 MB, verified via S3 API), idempotent second run including unchanged-size upload skip. Umlaut spot checks verified against raw source bytes.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
