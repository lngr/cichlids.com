---
id: TASK-5.20.4
title: Media library of a member
status: To Do
assignee: []
created_date: '2026-10-16 14:17'
labels: []
dependencies:
  - TASK-5.20.2
parent_task_id: TASK-5.20
priority: medium
ordinal: 105000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Every member has a media library with all files they own, linked or not (decided by the project owner, 2026-10-16). A fresh upload can sit in the library until it is linked, and a file stays there when its last link goes away. Only deleting a file from the library removes it with its renditions and all of its links, after a confirmation that names the places that show it. Files without links are visible to the owner only.

### Reference
- ADR-0029, TASK-5.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member whose care entry with a photo is deleted, When they open their media library, Then the photo is still there
- [ ] #2 Given a photo in a tank section and a story, When the owner deletes it from the library and confirms, Then it disappears from both and its files are removed
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
