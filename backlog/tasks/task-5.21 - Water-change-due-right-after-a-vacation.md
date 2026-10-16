---
id: TASK-5.21
title: Water change due right after a vacation
status: To Do
assignee: []
created_date: '2026-10-16 14:16'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: high
ordinal: 102000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The fish soil the water during a vacation as well, so vacation days count as normal days of a water change period (decided by the project owner, 2026-10-16). A vacation only helps when the deadline of the period falls into it: then the water change is due within the grace of the plan after the last vacation day. Formally the deadline of a period is the later of its regular deadline and the last vacation day plus the grace of the plan (one third of the interval rounded up). A vacation that ends before the regular deadline changes nothing. The water change that closes such a period counts like any other (verified or kept by a freeze) and starts a new period.

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
