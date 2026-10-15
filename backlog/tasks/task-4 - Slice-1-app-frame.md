---
id: TASK-4
title: Slice 1 app frame
status: To Do
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 08:55'
labels: []
dependencies: []
priority: high
ordinal: 71000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The relaunch app frame of `MVP-VISION.md` (sections 7 and 8, slice 1): navigation with Today,
Discover, +, Fish and Me; multiple languages, localized units and an RTL-ready layout; onboarding
with "Do you have a tank?" and standard tanks per country; the tank page after the hi-fi
template with showcase time-lapses from the archive; Discover with the archive and activity
sections behind a feature flag.

### Order
Feature flags and languages first (foundation), then navigation, units, onboarding, tank page,
Discover and Today.
<!-- SECTION:DESCRIPTION:END -->

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
<!-- SECTION:NOTES:END -->
