---
id: TASK-9.5
title: Community side of species pages
status: To Do
assignee: []
created_date: '2026-10-17 12:36'
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
