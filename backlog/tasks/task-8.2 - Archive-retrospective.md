---
id: TASK-8.2
title: Archive retrospective
status: In Review
assignee: []
created_date: '2026-10-17 09:50'
updated_date: '2026-10-17 10:56'
labels: []
dependencies: []
parent_task_id: TASK-8
priority: high
ordinal: 120000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A legacy member sees a personal retrospective of their time in the archive once, at the first login after the onboarding, and can open it again from Me.

### Content, in this order
- Member since: the year and month of profile.created_at; the ETL fallback marker 2003-01-01 is left out and the card then shows no date.
- Photos uploaded: the member's migrated gallery stories and migrated media items; comments received: comments by others on the member's stories and tanks, not deleted; stars received when the member has ratings.
- Most viewed photo: the member's story with the highest view_count, with its cover image and the count; left out when no story has views.
- Archive tanks: the member's tanks with a legacy id that are not adopted, each with the offer to adopt it (TASK-8.3 adds the flow; until then the offer opens the tank).
- Last card: the question how the tank looks today, leading to a new tank with a first photo (existing create and camera flows).
- Cards without data are left out; a member without any legacy data gets no retrospective.

### Behaviour
- GET /api/me/retrospectives/archive returns the cards; null for members without legacy data and for provisional profiles. POST /api/me/retrospectives/archive/seen stores profile.archive_retrospective_seen_at.
- The app opens the retrospective after the onboarding gate once while seen_at is empty; Me lists it while the member has legacy data.
- Presentation: the swipeable story presentation of the app (StorySequence or the tank story machine), full screen, with progress, pause and close; texts in all 11 catalogs.

### Reference
- MVP-VISION.md 6.1, TASK-8.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a legacy member with photos, comments and archive tanks, When the member signs in for the first time after the onboarding, Then the archive retrospective opens with the cards that have data, and does not open on later sign-ins
- [ ] #2 Given a member who closed the retrospective, When the member opens it from Me, Then it shows the same cards
- [ ] #3 Given a member without legacy data, When the member signs in, Then no retrospective opens and Me shows no entry
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
- Profile.ArchiveRetrospectiveSeenAt with a migration; GET /api/me/retrospectives/archive builds the cards from migrated content (member since without the 2003-01-01 marker, photos, comments and stars of others, most viewed story with cover, open archive tanks); 204 without data; POST .../seen stores the first time; GET /api/me reports available and seenAt.
- Integration tests for every card rule and the provisional gate.

### Client
- Regenerate the OpenAPI client; client-core maps the DTO to an ordered card list (the last card asks how the tank looks today) and offers a player machine (index, play, pause, seek) that other retrospectives reuse.
- Mobile: retrospective folder with a full screen card player (tap or swipe to advance, progress bars, pause, close), opened once by the onboarding gate while unseen, entry in the Me tab, texts in all 11 catalogs.

### Tooling and tests
- Golden snapshot: null the new column, pin the migration.
- client-core and mobile unit tests; one Playwright write E2E for first sign-in, reopen from Me and a member without legacy data.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- API: GET /api/me/retrospectives/archive (204 without data, 403 registration_required for provisional profiles), POST /api/me/retrospectives/archive/seen (stores the first time in profile.archive_retrospective_seen_at), GET /api/me reports archiveRetrospective with available and seenAt.
- Cards: member since (the 2003-01-01 marker leaves it out), photos (migrated photo media items that are not profile images), comments and stars of other members on the member's stories and tanks (deleted comments and own ones excluded), most viewed legacy story with cover and count, archive tanks that are published, not deleted and not adopted. A member whose cards are all empty has no retrospective.
- client-core: card mapping, player machine (retrospectivePlayerMachine), me.archiveRetrospective and me.markArchiveRetrospectiveSeen.
- Mobile: src/retrospectives (full screen player with progress bars, pause, close, tap and swipe, step buttons), route /retrospective/archive, opened once by the onboarding gate while unseen, entry in the Me tab, texts in the 11 catalogs.
- The archive tank card opens the tank page; the last card leads to /onboarding (create a tank, then the first photo).
- Golden snapshot nulls the new column and pins the migration.

### Decisions
- Stars received is the sum of the stars of ratings by others.
- The retrospective is marked seen when the screen has loaded it, not at close.
- It opens at the first gate check after sign-in once the first-tank onboarding is not offered; a member who completes the onboarding sees it at the next sign-in or app start.
- E2E helper logInThroughKeycloak closes the retrospective when a member with legacy content gets it; the onboarding spec marks it seen before opening a tank page.

### Tests
- API 2441 passed (1 skipped), ETL 351, Infrastructure 744, client-core 352 plus new, mobile 549, golden snapshot suites green, E2E write suite 55 specs green.
<!-- SECTION:NOTES:END -->
