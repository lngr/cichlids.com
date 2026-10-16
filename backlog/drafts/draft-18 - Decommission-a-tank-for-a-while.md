---
id: DRAFT-18
title: Decommission a tank for a while
status: Draft
assignee: []
created_date: '2026-10-16 12:44'
updated_date: '2026-10-16 14:15'
labels: []
dependencies: []
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A member can decommission a tank that stays in their profile but is not in use for a while, such as an empty breeding tank in the basement (raised by the project owner, 2026-10-16). While a tank is decommissioned its streaks neither count nor break, and the time does not count as operating time: a tank run for six months, decommissioned for one and run for one more has seven months of operation. This differs from a vacation, during which the tank keeps running.

### Public proof
Decommissioning is shown publicly on the profile of the tank with a photo that shows the empty tank, so others can check it; it cannot be faked by a hidden entry.

### Open questions
- Which proof the start needs (photo from the guided app camera, empty tank) and whether the end needs one as well.
- How public proof fits a tank whose profile is private or for followers only.
- Whether operating time is a figure shown on the tank and whether the tank year of the freezes (TASK-5.5) moves by the decommissioned time.
- Reminders and care plans during the decommissioned time.

### Reference
- TASK-5.5 (streaks, vacations, freezes per tank year), TASK-5.7 (visibility), TASK-5.11 (guided camera), DRAFT-17.
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
Decided by the project owner (2026-10-16): the decommissioning and its photo are visible to exactly the viewers who may see the tank and its streak.

Open (asked 2026-10-16, no preference given yet): whether the start photo must come from the app camera at the moment of decommissioning with checked provenance, as for the guided water change, or may be any photo.

Decided by the project owner (2026-10-16): the start photo comes from the app camera at the moment of decommissioning, with checked provenance as for the guided water change (in app, received by the server then, plausible capture time), with the hint to photograph the empty tank.
<!-- SECTION:NOTES:END -->
