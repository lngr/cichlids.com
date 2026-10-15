---
id: TASK-5.4
title: Today shows due care
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-15 20:37'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/today-care.write.mjs
parent_task_id: TASK-5
priority: medium
ordinal: 84000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Today (TASK-4.8) gains the due care of all tanks with one-tap logging and the streak above Your tanks, as in DRAFT-11.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member with a due water change, When they open Today, Then the due care appears above their tanks and one tap logs it
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
### Implementation (2026-10-15)
- The streak on Today moves to TASK-5.5, whose rule awaits a decision; Today has an empty StreakSlot between the greeting and the due care for it.
- Today shows a Due care section above Your tanks from GET /api/me/care/due: overdue care (longest first), care due today, then care due within 2 days; later care and never-logged rules stay out. A water change is logged with one tap with the confirmation and undo of TASK-5.1; a measurement opens the measurement form of TASK-5.2 for that tank. Logged items leave the list, and the section disappears when nothing is due. The care state machines are shared with the care view (useCareActors).
- Verification: client-core 76, mobile 260, both typechecks, all 25 write e2e specs green; the API is unchanged.
- Tests: app/client-core/src/carePlan.test.ts, app/mobile/e2e/playwright/today-care.write.mjs.
- Not verified: native Android and iOS runs, review of the translations by native speakers, remote CI.
<!-- SECTION:NOTES:END -->
