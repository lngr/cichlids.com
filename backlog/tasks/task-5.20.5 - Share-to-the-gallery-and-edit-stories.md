---
id: TASK-5.20.5
title: Share to the gallery and edit stories
status: In Review
assignee: []
created_date: '2026-10-16 14:17'
updated_date: '2026-10-16 20:02'
labels: []
dependencies:
  - TASK-5.20.3
  - TASK-5.20.4
references:
  - app/mobile/e2e/playwright/story-editor.write.mjs
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation (2026-10-16)
- media_item.quick_share_story_id records the quick-share story (unique); POST /api/me/media/{id}/share opens a short form in the app (title, topic) and publishes on confirm; deleting the story frees the file for a new quick share.
- Story editor under /api/me/stories: create, read, update, delete, withdraw (back to draft, slug and interactions stay), items (add own ready files, caption, remove link only), order of items and subjects in one sequence, cover among items, subjects (tank, inhabitant, species; invisible tanks answer like missing ones); publish refuses a story without items and keeps its slug.
- Delete: never published stories are deleted, ever published ones soft-deleted with their links dropped (slug alias, comments and ratings restrict); fixed: deleting a withdrawn story, its last file and the draft cleanup failed on that key.
- Limits: 30 items, 20 subjects, captions up to 500 characters.
- App: share action on own photos and videos (library, care, tank strips, time-lapse, videos, evidence), My stories under Me, editor with move buttons, captions, cover, subject cards (tanks and species; inhabitants API only).
- Verification (branch): .NET 643/2092 (1 skipped)/350, client-core 274, mobile 449, 47 write e2e specs green; texts of 10 catalogs worth a native-speaker pass.
<!-- SECTION:NOTES:END -->
