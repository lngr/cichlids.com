---
id: TASK-5.18
title: Daily water change reminders until the streak breaks
status: To Do
assignee: []
created_date: '2026-10-16 12:35'
updated_date: '2026-10-16 14:09'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 98000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
While a water change streak is running, the member gets a reminder every day at 09:00 local time from the due day of the plan period until the last day of its grace, each saying in how many days the streak breaks otherwise and pointing to entering a vacation (decided by the project owner, 2026-10-16). Logging the water change, a vacation or a snooze ends the reminders of that period. The same pattern applies to local app reminders (TASK-5.8) and to web push (TASK-5.12). Other care kinds keep the single reminder on the due day.

### Rules
- Grace is one third of the plan interval rounded up (TASK-5.5): a weekly plan is due on day 7 and the streak holds until day 10, so reminders come on days 7, 8, 9 and 10.
- The text names the remaining days ("breaks in 3 days", on the last day "breaks today") and offers to enter a vacation.
- Without a running streak the water change keeps the single reminder on the due day.

### Reference
- TASK-5.5 notes (periods, grace, vacations), TASK-5.8, TASK-5.12.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a running water change streak with a weekly plan, When the water change is not logged, Then the member gets one reminder per day at 09:00 from day 7 to day 10, each with the number of days until the streak breaks
- [ ] #2 Given such reminders, When the member logs the water change or enters a vacation, Then no further reminder of that period arrives
- [ ] #3 Given a browser with web push consent, When the same period runs, Then the web push reminders follow the same days and texts
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
### Decisions of the project owner (2026-10-16)
- The window starts at the deadline minus the grace, as built.
- An active vacation stops all water change reminders of the tank, as built.
- Streak reminders offer the familiar remind-me-tomorrow action; it dismisses the reminder of the day and the next planned one follows.
<!-- SECTION:NOTES:END -->
