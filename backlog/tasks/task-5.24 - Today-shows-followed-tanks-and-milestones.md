---
id: TASK-5.24
title: Today shows followed tanks and milestones
status: To Do
assignee: []
created_date: '2026-10-17 09:32'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 117000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Today gains two sections of the target in DRAFT-11 whose data exists: the updates of the tanks the member follows, and congratulations on milestones.

### Followed tanks
- The followed tanks with their newest update that the viewer may see: a care entry visible to the viewer, a story pointing to the tank, a new time-lapse or tank photo. Ordered by the newest update, at most five, with a link to all followed tanks.
- A tank without an update in the last 30 days is not shown. Visibility follows the rules of TASK-5.7 and TASK-5.15 and the media visibility of ADR-0029.

### Milestones
- First care week: the first week in which a tank met its plan.
- Kept streak: a streak of 4, 12, 26 or 52 weeks.
- Tank anniversary: each full tank year, counted as the freezes count it (decommissioned days shift it).
- A milestone shows on Today from the day it is reached for 7 days, until the member dismisses it; the dismissal is stored per member and milestone. Streak milestones follow the streak switch of TASK-5.19.

### Reference
- DRAFT-11, TASK-5.5, TASK-5.15, TASK-5.19, TASK-6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who follows tanks with fresh visible updates, When Today loads, Then the followed tanks section lists them by their newest update with that update
- [ ] #2 Given an update the member may not see, When Today loads, Then neither the update nor a hint of it appears
- [ ] #3 Given a tank that reached a streak of 4 weeks or a full tank year, When the owner opens Today within 7 days, Then a congratulation shows until the owner dismisses it
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
