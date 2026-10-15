---
id: TASK-4.6
title: Tank page after the hi-fi template with showcase time-lapses
status: To Do
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 08:57'
labels: []
dependencies: []
parent_task_id: TASK-4
priority: high
ordinal: 77000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The tank page follows `cichlids-tank-detail-hifi.html` (story and care views) and shows
time-lapses built from the archive photo series of a tank.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a legacy tank with a photo series, When a member opens it, Then the tank page shows the hi-fi layout and plays the time-lapse of its photos
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Hall of fame, tank of the week, showcase time-lapses (decided by the project owner, 2026-10-15)
- Hall of fame per region: the 20 tanks and 20 pictures with the best weighted (Bayesian) rating
  average; at least 10 ratings; self-ratings are already excluded (TASK-3.36).
- Tank of the week: each week another tank of the regional hall of fame, in rotation.
- Showcase time-lapses: tanks whose photos span at least one year with at least 8 photos from
  different months; the best rated of those.
- View counts do not count (distorted by crawlers in the legacy data); tanks and pictures of
  deleted or suspended members are excluded.
<!-- SECTION:NOTES:END -->
