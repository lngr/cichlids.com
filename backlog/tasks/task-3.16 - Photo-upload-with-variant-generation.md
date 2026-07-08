---
id: TASK-3.16
title: Photo upload with variant generation
status: To Do
assignee: []
created_date: '2026-07-08 18:58'
labels: []
dependencies:
  - TASK-3.10
  - TASK-3.13
  - TASK-3.9
parent_task_id: TASK-3
priority: high
ordinal: 36000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Authenticated photo upload creating a draft post with media item, variant generation into the object store, publish flow, discard of drafts.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an authenticated user on the local stack, When a photo is uploaded and published, Then a post with media item exists, variants are stored in the object store, and the picture appears in the gallery listing
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
