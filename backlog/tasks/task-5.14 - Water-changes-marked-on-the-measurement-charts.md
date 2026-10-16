---
id: TASK-5.14
title: Water changes marked on the measurement charts
status: In Review
assignee: []
created_date: '2026-10-16 06:28'
updated_date: '2026-10-16 07:01'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/water-change-marks.write.mjs
  - app/client-core/src/waterChangeMarks.test.ts
parent_task_id: TASK-5
priority: medium
ordinal: 94000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal (idea of the project owner, 2026-10-16)
The measurement charts of TASK-5.2 show the water changes of the tank as markers on the same time axis, so a member sees how a value such as nitrate rises between water changes and drops after each one (before and after). The chart works with whatever the member measures; no extra measurements are required. Tapping a marker opens the water change entry.

### Reference
- TASK-5.2 (charts and the follow-up to load all series in one request), TASK-5.1 and TASK-5.13 (water change entries).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a tank with nitrate measurements and water changes in between, When the member opens the nitrate chart, Then each water change appears as a marker on the time axis and the values before and after it are visible
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
- Pairing rule (decided by the project owner, 2026-10-16): a measurement counts as before or after a water change only when no other water change lies between them; otherwise that side stays empty. A measurement between two water changes is the after value of the first and the before value of the second.

### Implementation (2026-10-16)
- The measurement charts load the water changes of the tank through the care entries endpoint (50 per page, at most 10 pages, until the oldest measurement minus the 3-day window is covered) and draw each as a dashed vertical marker with a drop icon on the shared time axis; markers closer than 48 px use up to 3 lanes; each marker has a 48 by 48 target, a screen reader label and opens a small detail modal (date, amount, note).
- Before and after: the nearest measurement on each side within 3 days is ringed, and the values list states the change (for example nitrate 40 mg/l to 15 mg/l after the water change on ...), following the pairing rule above.
- Verification on the combined state with TASK-5.13: .NET 529/846 (1 opt-in benchmark skipped)/349 with 0 warnings, client-core 172, mobile 376, both typechecks, all 32 write e2e specs green.
- Tests: app/client-core/src/waterChangeMarks.test.ts, app/mobile/src/tanks/measurementFormat.test.ts, app/mobile/e2e/playwright/water-change-marks.write.mjs.
- Not verified: native rendering on Android and iOS.
<!-- SECTION:NOTES:END -->
