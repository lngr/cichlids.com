---
id: TASK-3.7
title: ETL migrates galleries to collections
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-08 23:20'
labels: []
dependencies:
  - TASK-3.5
parent_task_id: TASK-3
priority: medium
ordinal: 27000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
user_cichlids_gallery and its picture relation become collections with ordered entries.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given migrated media, When the gallery ETL step runs twice, Then each legacy gallery exists exactly once as a collection with its ordered pictures
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Real double run: 161 collections with 1001 entries (38 entries on non-migrated pictures skipped, 2 collections empty after filtering), hidden galleries mapped to non-public collections.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
