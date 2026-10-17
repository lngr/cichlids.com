---
id: TASK-9.2
title: Page and timeline of a named fish
status: To Do
assignee: []
created_date: '2026-10-17 12:36'
updated_date: '2026-10-17 13:50'
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
