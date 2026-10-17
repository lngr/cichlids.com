---
id: TASK-9.4
title: Fish Log
status: To Do
assignee: []
created_date: '2026-10-17 12:36'
labels: []
dependencies:
  - TASK-9.1
  - TASK-9.2
parent_task_id: TASK-9
priority: medium
ordinal: 128000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The Fish tab opens with the member's Fish Log: kept species, bred species and a wishlist, with numbers that reward good keeping (MVP-VISION.md 6.4).

### Content
- Kept: every species of the member's stocking history with the keeping time (sum of the periods with at least one inhabitant of the species, migrated inhabitants from the tank's creation) and the named fish with their age in the family (Bruno is 5).
- Bred: species with pure-bred spawning events and the highest generation; hybrids and fry counts do not count.
- Wishlist: species the member adds and removes.
- The number of species kept shows as a plain figure on the member's profile.
- No quantity goals and no progress per region.
- The species list stays reachable from the Fish tab.
- Visibility: the Fish Log of another member shows only what comes from tanks the viewer may see; the wishlist is public with the profile.

### Reference
- MVP-VISION.md 6.4, ADR-0031, TASK-9.1, TASK-9.2.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member with two species kept and Bruno, When the member opens the Fish tab, Then kept lists both species with their keeping time and Bruno with his age
- [ ] #2 Given a pure-bred spawning event with generation F2, When the Fish Log loads, Then bred lists the species with F2
- [ ] #3 Given another member's Fish Log, When a viewer may not see one of the tanks, Then nothing from that tank counts
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
