---
id: DRAFT-11
title: Today as in the product vision mockup once its content exists
status: Draft
assignee: []
created_date: '2026-10-15 09:01'
labels: []
dependencies: []
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Today becomes the care and activity hub of the mockup (`cichlids-tank-prototype.html`, care
dashboard) and of `MVP-VISION.md` (navigation table and "Engagement in der Anfangszeit"), as soon
as the content behind it exists. Slice 1 ships a reduced Today (TASK-4.8); this draft keeps the
target so it is built right away when the data is there.

### Target content (from the mockup and the vision)
- Care streak: days in a row, longest streak, care activity heatmap over all tanks (16 weeks).
- Due care per tank with a one-tap log (for example water change in 3 days).
- Followed tanks as a series: updates, milestones and new time-lapses, not single photos.
- Fresh entries of real members from the own regions, prominently, with kudos per tap.
- Questions from the own regions.
- System recognition: congratulations for milestones such as the first care week, a kept streak
  or a tank anniversary.

### Trigger
Implement each part in the slice that delivers its data: care log, plan, streak and reminders
(slice 2), follows and series updates, questions and kudos (slice 5); the reduced Today of
TASK-4.8 is replaced section by section, without waiting for all slices.

### Reference
- TASK-4.8 (reduced Today), decision of the project owner on 2026-10-15 to keep this target.
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
