---
id: TASK-8.2
title: Archive retrospective
status: To Do
assignee: []
created_date: '2026-10-17 09:50'
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
