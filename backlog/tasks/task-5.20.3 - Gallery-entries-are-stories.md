---
id: TASK-5.20.3
title: Gallery entries are stories
status: To Do
assignee: []
created_date: '2026-10-16 14:17'
labels: []
dependencies:
  - TASK-5.20.2
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
- API: list and detail return the cover, the items with captions and URLs and the visible subjects; the HTTP route names are decided before the build (see the epic).
- ETL and VerifyStep write and verify the story shape.
- App: gallery card with cover, story view with swipeable items, captions and cards.
<!-- SECTION:PLAN:END -->
