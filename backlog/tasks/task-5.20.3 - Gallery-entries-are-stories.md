---
id: TASK-5.20.3
title: Gallery entries are stories
status: In Review
assignee: []
created_date: '2026-10-16 14:17'
updated_date: '2026-10-16 18:30'
labels: []
dependencies:
  - TASK-5.20.2
references:
  - app/mobile/e2e/playwright/story.write.mjs
parent_task_id: TASK-5.20
priority: high
ordinal: 104000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Every gallery entry is a story: an ordered sequence of items (media links with role item, each with its own caption), subjects pointing to tanks, inhabitants and species (with a position as a card in the sequence, without one as a reference), and a cover chosen among its items, the first item when none is chosen (ADR-0029). A single-photo story is a story with one item; every archived picture is one.

### Reference
- ADR-0029, TASK-5.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an archived picture, When a visitor opens it in the gallery, Then it is a story with one item showing the same photo, title, text, comments and ratings at the same address
- [ ] #2 Given a story with three photos, a tank card and a chosen cover, When a visitor opens the gallery, Then the entry shows the cover, and the story shows the items with their captions and the card in order
- [ ] #3 Given a story whose tank subject is private, When a visitor without access opens it, Then the story shows without that card
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
- Rename post to story in the domain and the database (post_media items are media links already, post.tank_id and post_species become story_subject rows, post kind goes away); comments, ratings, collection entries, slug aliases and outbox events refer to the story.
- story_subject: story, subject type (tank, inhabitant, species) with typed nullable keys and an exactly-one check, optional position; items and subjects share one position sequence per story, kept by the application in one transaction.
- Cover: nullable reference from the story to one of its item links.
- API: list and detail return the cover, the items with captions and URLs and the visible subjects; the routes are named after stories (/api/stories and matching operation ids, decided by the project owner, 2026-10-16), the app follows in the same change, and public slug addresses stay.
- ETL and VerifyStep write and verify the story shape.
- App: gallery card with cover, story view with swipeable items, captions and cards.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation (2026-10-16)
- post becomes story (kind and tank_id gone, cover_media_link_id with ON DELETE SET NULL), post_species becomes story_subject (tank, inhabitant, species; exactly-one key; optional position; unique per story); post_id becomes story_id in comment, rating, collection_entry, slug_alias and media_link; outbox types and payload keys follow (story.published, storyId), webhook subscriptions rewritten; migration 20261016181720_GalleryStories with full Down and a test in both directions.
- Routes /api/stories, /api/stories/{slug}, comments, /api/stories/{id}/publish and DELETE, /api/profiles/{id}/stories; list carries cover and itemCount; detail carries items with captions and the subjects the viewer may see (tank and inhabitant subjects by the viewer-aware tank visibility of TASK-5.15, species public).
- App: gallery card with cover and item count, story pager with captions and positioned cards, references below the text.
- Web addresses /gallery/<slug>, public slugs and legacy redirects stay; mobile test ids picture-* stay.
- Interpretations: see the journal F21 (video limit counts published stories; Discover excludes stories with a non-public tank subject; cover membership is kept by the application and checked by VerifyStep).
- Verification (branch on main): .NET 639/2039 (1 skipped)/350, client-core 258, mobile 440, 45 write e2e specs green.
<!-- SECTION:NOTES:END -->
