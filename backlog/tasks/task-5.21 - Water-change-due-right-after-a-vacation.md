---
id: TASK-5.21
title: Water change due right after a vacation
status: In Review
assignee: []
created_date: '2026-10-16 14:16'
updated_date: '2026-10-16 14:36'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/streak-vacation-deadline.write.mjs
  - >-
    app/api/tests/Cichlids.Api.Tests/Features/Streaks/WaterChangeVacationTests.cs
parent_task_id: TASK-5
priority: high
ordinal: 102000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The fish soil the water during a vacation as well, so vacation days count as normal days of a water change period (decided by the project owner, 2026-10-16). A vacation only helps when the deadline of the period falls into it: then the water change is due within the grace of the plan after the last vacation day. Formally the deadline of a period is the later of its regular deadline and the last vacation day plus the grace of the plan (one third of the interval rounded up). A vacation whose last day plus the grace lies before the regular deadline changes nothing. The water change that closes such a period counts like any other (verified or kept by a freeze) and starts a new period.

### Examples (weekly plan, grace 3 days, water change on day 0, regular deadline day 10)
- Vacation from day 2 to day 6: deadline day 10.
- Vacation from day 5 to day 20: deadline day 23.
- Vacation from day 8 to day 9: deadline day 12.

### Unchanged
- During an active vacation the streak shows as on vacation and no water change reminder arrives; from the first day after the vacation the daily reminders run until the deadline (TASK-5.18).
- A vacation entered afterwards may repair the streak only when it starts at most 7 days before it is entered (TASK-5.5).
- Freezes per tank year (TASK-5.5).

### Reference
- TASK-5.5, TASK-5.18.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a weekly plan and a vacation from day 2 to day 6 after a water change on day 0, When no water change follows by day 10, Then the streak breaks on day 11
- [ ] #2 Given a weekly plan and a vacation from day 5 to day 20 after a water change on day 0, When the water change follows on day 23, Then the streak continues, and without it the streak breaks on day 24
- [ ] #3 Given a vacation that ends, When the first day after it begins, Then the member gets the daily reminders with the days left until the new deadline
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
- Domain: the deadline of a period is the regular deadline, moved to the last day of a vacation plus the grace when later; a vacation counts when it starts on or before the current deadline and ends after the period start, applied until the deadline stops moving; an open vacation leaves the period without a deadline (Paused). Bridged arises only from a switched off rule.
- Reminder window after a vacation starts on the day after it (FirstDayAfterVacation) or the regular start when later.
- Texts: vacation intro in all 11 catalogs.
- Verification: .NET 554/1260 (1 skipped)/349, 0 warnings; client-core 242, mobile 414, both typechecks; 38 write e2e specs green.
- Tests: WaterChangeVacationTests.cs, CareDueStreakTests.cs, streak-vacation-deadline.write.mjs.
<!-- SECTION:NOTES:END -->
