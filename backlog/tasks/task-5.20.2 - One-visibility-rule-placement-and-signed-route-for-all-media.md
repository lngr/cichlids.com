---
id: TASK-5.20.2
title: 'One visibility rule, placement and signed route for all media'
status: To Do
assignee: []
created_date: '2026-10-16 14:17'
labels: []
dependencies:
  - TASK-5.20.1
parent_task_id: TASK-5.20
priority: high
ordinal: 103000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Whether a viewer may see a media item follows from one rule: the union of the visibility of its links, and the owner sees all own files (ADR-0029). Storage placement follows from the same rule, per media item, and all private photos and videos of every context go through one signed route.

### Reference
- ADR-0029, TASK-5.7 (visibility levels, only-me entries), TASK-5.20.1.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a photo linked to a private tank and to a published story, When an anonymous viewer opens the story, Then the photo loads from the public area, and the tank stays hidden
- [ ] #2 Given a photo whose only link is a care entry of a tank with a public history, When the entry is set to only me, Then the photo moves to the private area and only the owner can load it through a signed link
- [ ] #3 Given a signed link issued for a viewer, When it expires or the media item is deleted, Then the route refuses it
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
- Visibility mapping per context: tank (profile visibility; the decommission and recommission proof photos of TASK-6 follow the history visibility like the streak they prove), care_entry and water_change_session (history visibility and only me), inhabitant (its tank), post (published: everyone, draft: author), discussion_post (not removed), profile (not suspended or deleted). Unlinked: owner only.
- One query building block answers the visibility of many media items for one viewer, so lists stay fast; Discover keeps using public URLs only.
- Placement: queue columns on media_item (pending, next attempt, attempts, placed public); every change that can alter the answer marks the affected items (link created or removed, tank or history visibility change, only me toggled, story published, withdrawn or removed); one hosted worker moves variants and HLS directories; originals stay where they are.
- One signer (Data Protection purpose per media variant or HLS directory, photo 30 minutes, video 2 hours) and one route family for photos and HLS files; the old tank-media, tank-videos, care-photos and care-videos routes go away; client-core path handling and the e2e specs follow.
<!-- SECTION:PLAN:END -->
