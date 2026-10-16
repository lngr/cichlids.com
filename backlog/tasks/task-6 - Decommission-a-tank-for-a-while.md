---
id: TASK-6
title: Decommission a tank for a while
status: In Review
assignee: []
created_date: '2026-10-16 12:44'
updated_date: '2026-10-16 15:38'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/decommission.write.mjs
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

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a running water change streak, When the member decommissions the tank with an app camera photo of the empty tank, Then the streak neither counts nor breaks and visitors who see the streak see the decommissioning with the photo
- [ ] #2 Given a decommissioned tank, When the member puts it back into operation with an app camera photo of the filled tank, Then the plan runs from that day and the streak continues with the next water change
- [ ] #3 Given a tank run for six months, decommissioned for one and run for one more, When a visitor opens the tank page, Then it shows seven months of operation
- [ ] #4 Given decommissioning, When the member chooses that all care pauses, Then the tank has no due care, is not on Today and gets no reminders
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
Decided by the project owner (2026-10-16): the decommissioning and its photo are visible to exactly the viewers who may see the tank and its streak.

Open (asked 2026-10-16, no preference given yet): whether the start photo must come from the app camera at the moment of decommissioning with checked provenance, as for the guided water change, or may be any photo.

Decided by the project owner (2026-10-16): the start photo comes from the app camera at the moment of decommissioning, with checked provenance as for the guided water change (in app, received by the server then, plausible capture time), with the hint to photograph the empty tank.

Decided by the project owner (2026-10-16): ending the decommissioning needs proof as well, a photo of the filled tank from the app camera at that moment with checked provenance, visible to the viewers who see the streak; the plan runs from that day and the streak continues with the next water change.

Decided by the project owner (2026-10-16): the tank page shows the operating time without decommissioned days, and the tank year of the freezes counts operating days, so it moves by the decommissioned days.
Decided by the project owner (2026-10-16): when decommissioning, the app asks the member whether all care pauses (no due care, not on Today, no reminders, heatmap shows the time as decommissioned) or only the water change and its streak pause while measurements stay due with their reminders.

### Implementation (2026-10-16)
- Domain: Decommissions, DecommissionProofs, DecommissionScope; WaterChangeStreaks.Evaluate skips decommissioned days [start, end) and ignores water changes on them; after reactivation the period runs from the reactivation day; TankYear counts operating days (creation date shifted by earlier decommissioned days).
- Storage: tank_decommissioning; proof photos as tank_media sections decommission and recommission with captured_in_app (mapped to roles by TASK-5.20.1).
- API: GET /api/tanks/{id}/decommissioning, POST .../decommissioning, POST .../decommissioning/end; visible with the care stream (404 otherwise); provenance: app flag, EXIF within 10 minutes of receipt (plus 14 h without offset), checksum not used before.
- Care: due list, plan, Today, reminders and heatmap follow the chosen scope; proof photos are excluded from time-lapse, galleries and counts.
- App: tank card with operating time and history, decommission screen (scope question without preselection, then camera), heatmap rest cells, badge on the tank card; 11 catalogs.
- Operating months: whole calendar months per stretch of operation plus leftover days as 30-day months.
- Known gap: proof photos follow the profile-level storage like time-lapse photos; TASK-5.20.2 places them by the history visibility.
- Verification: .NET 559/1326 (1 skipped)/349, 0 warnings; client-core 250, mobile 430, typechecks; 39 write e2e specs green. Android and iOS not run.
<!-- SECTION:NOTES:END -->
