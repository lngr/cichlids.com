---
id: TASK-8.3
title: Adopt archive tanks with suggested photos
status: In Review
assignee: []
created_date: '2026-10-17 09:50'
updated_date: '2026-10-17 11:32'
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

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### API
- GET /api/me/legacy-tanks lists only published, not deleted, not adopted archive tanks.
- GET /api/me/legacy-tanks/{id}/photo-suggestions: migrated photos of the member that are not linked to the tank or a profile, ordered by capture time, then story published or created time, then media item time; one batched read of the date columns, paging in memory over that list, one batched image read for the page.
- POST /api/me/legacy-tanks/{id}/adopt: one transaction with row locks of the profile and the tank; a second adoption answers 409; the chosen media items are locked and checked for ownership (media_not_found, nothing linked) and linked with the showcase role in the chosen order after the existing showcase links; adopted_at, tank.adopted event and the returned badge (awarded only the first time).
- No schema change, so no migration and no golden snapshot change.

### Client
- Regenerate the OpenAPI client; client-core maps suggestions and the adoption result.
- Mobile: adoption flow (confirm, grid of suggestions with dates and ordered multi-select, adopt) opened from the archive retrospective tank card and from the member's own archive tank page while it is not adopted; texts in all 11 catalogs.

### Tests
- API integration tests per acceptance criterion plus 409, others' media, list filter, onboarding path, ordering with fallbacks and paging; client-core and mobile unit tests; one Playwright write E2E.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- API: GET /api/me/legacy-tanks lists only published, not deleted, not adopted archive tanks (the onboarding uses the same list). GET /api/me/legacy-tanks/{id}/photo-suggestions returns a page of the migrated photos of the member that are linked neither to the tank nor to the profile, ordered by capture time, then the published or created time of their story, then the time of the media item; the date columns are read in one batch, the order is applied in memory and one batched read resolves the images of the page. POST /api/me/legacy-tanks/{id}/adopt runs in one transaction under row locks of the profile and the tank, links the chosen photos with the showcase role in the chosen order after the existing showcase links, sets adopted_at, emits tank.adopted and awards the returned badge only when the member does not hold it. Registered members only.
- Refusals: legacy_tank_not_found (404), tank_already_adopted (409, also for the suggestions of an adopted tank), media_not_found (404, a file that is not an own photo), adoption_photos_invalid (400, duplicates, ids below 1, more than 100), media_already_linked (400). Nothing is stored on a refusal.
- The linking lives in MediaLibraryService.LinkPhotosToTankAsync (row lock and owner check of the files), the image read in MediaLibraryQueryService.PhotoImagesAsync.
- No schema change: no migration, no golden snapshot change.
- client-core: me.photoSuggestions, me.adoptLegacyTank, toggleSuggestion (ordered choice), adoptionProblemOf.
- Mobile: route /tanks/{id}/adopt (confirm, grid of suggestions with dates and numbered multi-select, adopt), opened from the archive tank card of the retrospective and from an offer card on the own archive tank page while it is not adopted; the tank page shows the photos afterwards; texts in the 11 catalogs.

### Decisions
- Profile images (avatar and header) are not suggested, as in the archive retrospective count.
- Adopting without photos is allowed.
- The adoption does not change the interests of the profile; the region refinement stays on the tank page.
- Capture times show the calendar day as recorded; other dates show in the device zone.
- The offer on the tank page asks me.legacyTanks instead of a new field of the tank detail.

### Tests
- API: AdoptArchiveTankTests (12 cases) and the provisional route declaration; .NET Api 2526, Infrastructure 744, Etl 351 green.
- client-core 365, mobile 566; write E2E adopt-archive-tank.write.mjs and the full write suite (57 specs) green.
- Not verified: native Android and iOS runs, remote CI.
<!-- SECTION:NOTES:END -->
