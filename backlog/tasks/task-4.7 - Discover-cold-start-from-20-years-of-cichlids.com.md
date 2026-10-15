---
id: TASK-4.7
title: Discover cold start from 20 years of cichlids.com
status: In Progress
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 11:34'
labels: []
dependencies: []
parent_task_id: TASK-4
priority: medium
ordinal: 78000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Discover presents the archive: hall of fame per region, tank of the week from the archive,
showcase time-lapses; activity sections (new tanks, active members, counters) exist behind the
feature flag.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an environment with the archive and the activity flag off, When a member opens Discover, Then they see the hall of fame per region, the tank of the week and the showcase time-lapses, and no activity sections
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
### Regions (decided by the project owner, 2026-10-15)
- Lake Malawi, Lake Tanganyika, Lake Victoria, Africa other (West Africa, rivers, Madagascar),
  Central America, South America, Mixed (for example mbuna with haps, community tanks).
- Legacy tanks: malawi, tanganyika map directly; african to Africa other; community to Mixed;
  central_american and south_american directly; legacy "american" without detail becomes
  "Americas (unassigned)", which the member refines with one tap on their tank; until then such
  tanks count for both American regions in the hall of fame.

### Hall of fame, tank of the week, showcase time-lapses (decided by the project owner, 2026-10-15)
- Hall of fame per region: the 20 tanks and 20 pictures with the best weighted (Bayesian) rating
  average; at least 10 ratings; self-ratings are already excluded (TASK-3.36).
- Tank of the week: each week another tank of the regional hall of fame, in rotation.
- Showcase time-lapses: tanks whose photos span at least one year with at least 8 photos from
  different months; the best rated of those.
- View counts do not count (distorted by crawlers in the legacy data); tanks and pictures of
  deleted or suspended members are excluded.

### Region of a picture (decided by the project owner, 2026-10-15)
- No rated picture of the archive is linked to a tank; 21,704 of 53,338 pictures with at least 10 ratings carry an identified species.
- A picture belongs to the region of its linked tank, otherwise to the region of its identified species (species category: lake malawi, lake tanganyika, african to Africa other, american to both American regions).
- Pictures without tank and species appear in no regional list. Discover additionally shows a cross-region top 20 of pictures in which every eligible picture counts.
<!-- SECTION:NOTES:END -->
