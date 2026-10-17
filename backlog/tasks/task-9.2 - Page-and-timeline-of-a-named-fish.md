---
id: TASK-9.2
title: Page and timeline of a named fish
status: In Review
assignee: []
created_date: '2026-10-17 12:36'
updated_date: '2026-10-17 14:41'
labels: []
dependencies:
  - TASK-9.1
parent_task_id: TASK-9
priority: high
ordinal: 126000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A named fish has a page with its name, species, photos and videos and a timeline: moved in, growth with a length, spawning with the generation of the offspring, a move to another tank of the same member, death. After its death the page stays as a memorial (ADR-0031).

### Scope
- Media links in the inhabitant context with the roles photo and video (ADR-0029); the owner links files from the media library or the camera; the page shows them newest first.
- Events: growth (length in cm or inch with the stored input unit), spawning (offspring generation F1, F2 and so on, or hybrid; pure-bred unless hybrid), move to another tank of the owner (the fish changes its tank, the move is an event; a dialog says that its photos then follow the visibility of the new tank), death (removes it from the stocking with the reason died). Anniversaries of the moved-in day show on the timeline without being stored.
- The page is visible to the viewers of the current tank, or of the last tank after death; deleting the tank keeps the page.
- Today shows a congratulation on the anniversary of a named fish (milestone kind of TASK-5.24, dismissable).
- Route /fish/{id} in the app, linked from the stocking list.

### Reference
- ADR-0029, ADR-0031, TASK-5.24, TASK-9.1.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a named fish with photos and a growth entry, When a viewer of its tank opens its page, Then the page shows the photos and the timeline with the growth
- [ ] #2 Given a named fish that moves to another tank of its owner, When the move is saved, Then its page, photos and timeline stay one and the timeline shows the move
- [ ] #3 Given a named fish that died, When a viewer opens its page, Then it shows as a memorial with its dates
- [ ] #4 Given a viewer who may not see the tank, When the viewer opens the page, Then the API answers not found
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Data model
- Table inhabitant_event: id, inhabitant_id (cascade), kind (growth, spawning, moved, died), occurred_on (date), created_at, plus per kind: length_cm and input_value with input_unit (cm or inch) for growth, offspring_generation or offspring_hybrid for spawning, from_tank_id and to_tank_id for a move. Check constraints tie the columns to the kind; the tank keys restrict.
- The role check of media_link gains photo and video for the inhabitant context. A trigger on inhabitant changes the visibility generation of the media of a fish whose tank changes, so signed links follow the new tank.
- Anniversaries of moved_in_on are computed, never stored.

### API
- GET /api/fish/{id}: header, memorial state, photos and videos newest first, typed timeline (kind, date, per-kind detail; the list is the place where entries and stories of the tank join). A fish whose tank the viewer may not see answers the 404 of a missing fish. The owner reads own fish also after the tank was deleted.
- Owner writes: POST /api/fish/{id}/growth, /spawning, /move, /death, DELETE /api/fish/{id}/events/{eventId} (growth and spawning), POST /api/fish/{id}/media and DELETE /api/fish/{id}/media/{mediaItemId}. A move changes the tank to another live tank of the owner and records the event in one transaction; death sets removed_on and the reason died and records the event, as the stocking removal with the reason died does for a named fish.
- Milestone kind fish_anniversary:{inhabitant}:{year} on Today: only reached years, exact canonical form, dismissable by the owner.

### Clients and tests
- OpenAPI client regenerated, client-core mapping, mobile fish page at /fish/{id} linked from the stocking list, texts in all 11 catalogs.
- Golden snapshot rules and pinned migration; API integration tests per acceptance criterion; client-core and mobile unit tests; one Playwright write spec.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- Migration NamedFishEvents: table inhabitant_event (kinds growth, spawning, moved, died; growth stores length_cm plus the typed value and unit cm or inch; spawning stores the generation or the hybrid flag; a move stores from and to tank; one death per fish), the role check of media_link gains photo and video for the inhabitant context, a trigger changes the visibility generation of the media of a fish when its tank changes, and named fish that already died get their died event.
- API: GET /api/fish/{id} (header, memorial state, photos and videos newest first, typed timeline newest first), owner writes POST /api/fish/{id}/growth, /spawning, /move, /death and /media, DELETE /api/fish/{id}/events/{eventId} (growth and spawning only) and DELETE /api/fish/{id}/media/{mediaItemId}. Every write answers with the page. A move changes the tank to another live tank of the owner and records the event in one transaction; a death sets removed_on and the reason died and records the event; the stocking removal with the reason died records the event for a named fish too. Writes lock the tank rows by id, then the fish row.
- Visibility: the owner always reads the own fish, also after the tank was deleted; everyone else needs a published, not deleted tank whose profile level reaches them. A hidden fish, a group and a missing id answer the same 404 fish_not_found. A move event names a tank only for viewers who may see it.
- Media: the owner links photos and videos of the own media library (MediaLibraryService.LinkToInhabitantAsync); the camera and the device upload put the photo into the library first. The library names the fish page as the place of a file.
- Today: milestone kind fish_anniversary:{inhabitant}:{year}. Only completed years of current named fish of live tanks, exact canonical key (positive subject, year 1 to 100, no leading zeros), the dismissal of a key checks ownership and that the year is reached.
- Clients: OpenAPI client regenerated, client-core fish.ts (mapping, growth and spawning forms, calls under client.fish), mobile route /fish/{id} linked from the stocking list (named fish), sheets for photo (library, camera, device), growth, spawning, move (warns that photos follow the visibility of the new tank) and death, memorial state, lengths in the unit system of the viewer (the owner reads the typed value), texts in all 11 catalogues.
- Golden snapshot: the events are kept, events that name a tank with a restricted profile level are removed, trigger and function pinned in routines.json, migration pinned.

### Extension point for TASK-9.3
- Timeline items are FishTimelineItemDto (Features/Fish/FishDtos.cs) with the enum FishTimelineKind. FishTimeline.Items builds the items of the fish itself (move-in, stored events, anniversaries, departure); FishService.GetAsync calls it and sorts with FishTimeline.Order. TASK-9.3 adds the kinds care_entry and story to FishTimelineKind, nullable detail members to FishTimelineItemDto, creates its items in FishService.GetAsync (batched, visibility of entries and stories applied there) and passes the union to FishTimeline.Order. The same extension is needed in client-core fish.ts (toTimelineItem), mobile fishText.timelineText and FishScreen iconOf.

### Decisions
- Timeline order is newest first, ties on one day by life order (death, move, other items, move-in).
- Media order is newest first by the creation time of the file.
- Growth stores centimetres (numeric 7,3) with the typed value and unit; a length is positive and at most 300 cm.
- Spawning: generation 1 to 99 for pure-bred, or hybrid without a generation.
- A death day is not before the latest event of the fish; every event day is not in the future and not before the move-in day.
- Writes on a dead fish answer 409 fish_removed except media and event deletion; a move to a foreign, deleted, missing or the same tank answers 400 fish_move_tank_invalid.
- A fish that was given away or moved on the stocking route shows a derived left item and no memorial.
- After the tank was deleted the page is visible to the owner only; the owner can still move the fish to a live tank.
- Provisional owners write like owners of the stocking (private content), so the routes need no registration.

### Verification
- API 2668 passed (1 skipped), Infrastructure 750, ETL 351, client-core 420, mobile 612, golden snapshot tests green in postgres:18.6 (snapshot, restore, media, migrated schema, provisioning), full write E2E 62 specs green including fish-page.write.mjs.
- The failing tests were written together with the code; the red phase was not run separately.
<!-- SECTION:NOTES:END -->
