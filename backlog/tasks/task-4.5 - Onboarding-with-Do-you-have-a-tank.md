---
id: TASK-4.5
title: Onboarding with Do you have a tank
status: To Do
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 09:01'
labels: []
dependencies: []
parent_task_id: TASK-4
priority: high
ordinal: 76000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A new member answers "Do you have a tank?". Yes: the first tank exists in under two minutes from
a standard tank of the country (US gallon sizes, European liter sizes with dimensions) or by
taking over a legacy tank; its region sets the interests (with "mixed"). No: the member picks
interests. The first entry is rewarded at once.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a new member with a tank, When they answer yes, pick a standard tank and a region, Then their first tank exists with the dimensions of that standard tank and the app shows the reward
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

### Reward of the first entry (decided by the project owner, 2026-10-15)
- A short success moment right after saving: a calm animation (no confetti storm) with the tank
  in the center and "Your tank is created. From now on it tells its story."
- A first badge in the profile: "First tank", for returning legacy members "Returned"; the start
  of the recognition logic that slice 5 extends.
- A direct next step: "Add your first photo".
- No points and no leaderboards.
<!-- SECTION:NOTES:END -->
