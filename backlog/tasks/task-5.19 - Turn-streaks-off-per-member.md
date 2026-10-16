---
id: TASK-5.19
title: Turn streaks off per member
status: In Review
assignee: []
created_date: '2026-10-16 12:38'
updated_date: '2026-10-16 15:38'
labels: []
dependencies:
  - TASK-5.18
references:
  - app/mobile/e2e/playwright/streak-switch.write.mjs
parent_task_id: TASK-5
priority: medium
ordinal: 99000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A member can turn the streaks off with one switch in their settings (decided by the project owner, 2026-10-16). With streaks off, no streak appears in the member's own views (tank, Today, Me) or to anyone else (tank page, profile), and the daily water change reminders of TASK-5.18 fall back to the single reminder on the due day, in the app and by web push. The streaks are still evaluated in the background, so turning them on again shows the true state with its history. Streaks are on by default.

### Reference
- TASK-5.5 (streaks), TASK-5.18 (daily reminders), TASK-5.8, TASK-5.12.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member with a running streak, When they turn streaks off, Then neither they nor visitors see a streak on the tank page, Today, Me or the profile
- [ ] #2 Given streaks off, When a water change is due, Then the member gets only the single reminder on the due day
- [ ] #3 Given streaks turned off for a while, When the member turns them on again, Then the streak shows the state it would have had all along
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
### Implementation (2026-10-16)
- profile.streaks_enabled (default true), GET/PUT /api/me/settings; with streaks off no streak, vacation share or reminder window reaches anyone, the evaluation still runs and records freezes; the app switch lives in the settings of Me and rebuilds the local reminders.
- Verification (branch): .NET 554/1270 (1 skipped)/349, client-core 245, mobile 414, 39 write e2e specs green.
<!-- SECTION:NOTES:END -->
