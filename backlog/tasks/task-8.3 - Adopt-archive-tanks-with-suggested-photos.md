---
id: TASK-8.3
title: Adopt archive tanks with suggested photos
status: To Do
assignee: []
created_date: '2026-10-17 09:50'
labels: []
dependencies:
  - TASK-8.2
parent_task_id: TASK-8
priority: high
ordinal: 121000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A legacy member adopts any of their archive tanks outside the onboarding, confirms it and assigns suggested photos from their archive, sorted by capture date.

### Scope
- GET /api/me/legacy-tanks lists only tanks that are not adopted (the onboarding uses the same list).
- GET /api/me/legacy-tanks/{id}/photo-suggestions: the member's migrated media items that are not linked to the tank, sorted by capture time (taken_at), falling back to the published or created time of their story, then the media item; paged.
- POST /api/me/legacy-tanks/{id}/adopt with the chosen media item ids: sets adopted_at, emits tank.adopted, awards the returned badge the first time as the onboarding does, and links the chosen photos to the tank with the showcase role in the chosen order. Only own media items; a second adoption answers 409.
- The visibility of an adopted archive tank stays as it is (public and followable, TASK-5.7).
- App: the offer in the archive retrospective and on the member's archive tank opens a flow: confirm the tank, pick suggested photos in a grid with their dates, adopt; the tank page then shows the photos.

### Reference
- MVP-VISION.md 6.2, TASK-4.5, TASK-5.9, ADR-0029 (media links).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member with an archive tank and migrated photos not linked to it, When the member opens the adoption, Then the photos are suggested sorted by capture date
- [ ] #2 Given chosen suggestions, When the member adopts the tank, Then the tank is adopted, the photos are linked to it in the chosen order, and the tank leaves the list of archive tanks
- [ ] #3 Given a media item of another member, When it is sent with an adoption, Then the API refuses the adoption and links nothing
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
