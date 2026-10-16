---
id: TASK-5.20.5
title: Share to the gallery and edit stories
status: To Do
assignee: []
created_date: '2026-10-16 14:17'
labels: []
dependencies:
  - TASK-5.20.3
  - TASK-5.20.4
parent_task_id: TASK-5.20
priority: medium
ordinal: 106000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Any own photo or video (from a tank, a care entry, a water change or the library) can be shared to the gallery quickly, which creates a single-item story; this quick share exists once per media item, and afterwards the item shows that it is in the gallery with a link to that story (decided by the project owner, 2026-10-16). Adding an own media item to further stories is possible any number of times. The story editor creates and edits stories: items from the library, order, captions per item, cover, subjects (own and other members' tanks and inhabitants, species) as cards or references, draft and publish.

### Reference
- ADR-0029, TASK-5.20, TASK-5.7 notes (gallery and tank photos).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an own care photo not yet shared, When the member shares it to the gallery, Then a single-item story with that photo is created, and the photo shows a link to it instead of the share action
- [ ] #2 Given the same photo, When the member adds it to a second story with another caption, Then both stories show the photo, each with its own caption
- [ ] #3 Given a story draft with items, a tank card and a chosen cover, When the member publishes it, Then the gallery lists it with that cover
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
