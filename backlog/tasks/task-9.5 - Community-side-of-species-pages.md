---
id: TASK-9.5
title: Community side of species pages
status: In Review
assignee: []
created_date: '2026-10-17 12:36'
updated_date: '2026-10-17 13:05'
labels: []
dependencies: []
parent_task_id: TASK-9
priority: medium
ordinal: 129000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A species page shows what the community does with the species, from data that exists: the best photos, how many tanks keep it, stocking partners from real tanks, archive questions and answers, and members who bred it (MVP-VISION.md 6.4).

### Scope
- Best photos: published stories with the species as a subject, ordered by rating and views, at most twelve, with a link to the gallery filter.
- Tanks keeping it: count of visible published tanks whose current stocking has the species, and a few of them.
- Stocking partners: the species most often kept together with it in visible tanks, with the number of tanks; at least three tanks per partner, so no single tank is exposed.
- Archive questions: forum threads of the archive that mention the species by name, if the archive search exists; otherwise left out.
- Breeders: members with a pure-bred spawning event of the species (TASK-9.2) on visible tanks, once such data exists.
- Only public and visible data; counts never include hidden tanks.
- The species card with traits stays as it is; this story adds sections below it.

### Reference
- MVP-VISION.md 6.4, TASK-3.12, ADR-0029.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a species with rated stories and tanks keeping it, When a visitor opens the species page, Then it shows the best photos, the number of tanks and stocking partners with at least three tanks each
- [ ] #2 Given a private tank keeping the species, When a visitor opens the page, Then the tank counts nowhere
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
### Approach
- One anonymous endpoint GET /api/species/{idOrSlug}/community returns the community sections of a species page. The data is the same for every viewer, so it contains public data only.

### API
- Visible tanks: TanksVisibleTo(null), published, not deleted, owner is a Member profile that is not suspended (provisional owners are excluded by the kind).
- Current stocking lives in one place, CurrentInhabitants.Query(context), an IQueryable over inhabitant rows. Every stocking query of this story reads it. Every inhabitant row is current until the inhabitant model carries move-in and removal dates; the rule is changed in that one helper.
- Best photos: published, not deleted stories with the species as a subject, by a browsable author, without a tank subject that is not public, with a cover photo that anonymous viewers see. Ordered by the Bayesian rating score (RatingScore), then view count, then id; at most twelve. The link to the gallery filter is built by the app from the species slug.
- Tanks keeping it: count of distinct visible tanks with a current inhabitant of the species, plus the six most recently published as cards with the main or first showcase image.
- Stocking partners: other species in those tanks, counted in distinct tanks, only those in at least three tanks, ordered by tank count, then name, at most ten. One grouped query.
- No new index: the foreign key indexes of inhabitant (species_id, tank_id) serve the joins.
- Left out: archive questions (no search over the discussion archive exists) and breeders (need the spawning events of TASK-9.2).

### Client
- OpenAPI client regenerated; client-core maps the response; the species page shows the sections below the species card (photos strip with gallery link, tank count with tank cards, partner chips), texts in all 11 catalogs.

### Tests
- API integration tests per acceptance criterion plus visibility levels, suspended and provisional owners, the three-tank threshold and ordering; client-core and mobile unit tests; one Playwright write spec.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- GET /api/species/{idOrSlug}/community (anonymous, same answer for every caller): best photos (max 12, Bayesian rating score, then views), count of public tanks keeping the species with the six newest as cards, stocking partners in at least three public tanks (distinct tanks, most common first, max ten).
- CurrentInhabitants.Query is the single place that defines which inhabitant rows are current; all stocking queries read it.
- Public tanks: TanksVisibleTo(null), published, not deleted, owner a Member profile that is not suspended (provisional, archived and system owners are excluded).
- Photo stories: published, topic cichlids or tanks (as the gallery), browsable author, no tank subject outside the public tanks, cover photo that anonymous viewers see.
- RatingScore.OfStory is the shared ordering expression of the gallery and the species page.
- No new index: the foreign key indexes of inhabitant serve the joins.
- OpenAPI client regenerated; client-core has species.community; the species page shows the sections below the species card, texts in all 11 catalogs.

### Left out
- Archive questions: no search over the discussion archive exists.
- Breeders: need the spawning events of TASK-9.2.

### Tests
- API integration tests for visibility levels, draft and deleted tanks, suspended and provisional owners, the three-tank threshold, ordering, the photo cap and not-found; client-core and mobile unit tests; Playwright write spec species-community.write.mjs; full write E2E (59 specs) green.
<!-- SECTION:NOTES:END -->
