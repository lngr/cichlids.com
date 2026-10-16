---
id: TASK-5.16
title: Record measurements and care afterwards
status: In Progress
assignee: []
created_date: '2026-10-16 07:10'
updated_date: '2026-10-16 07:43'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/care-when.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Care/CareTimeTests.cs
parent_task_id: TASK-5
priority: high
ordinal: 96000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal (requested by the project owner, 2026-10-16)
Members often note water values and enter them a day or two later. The measurement form and the quick log offer the time of the measurement or care entry: now by default, or a chosen earlier date and time (not in the future). The API already accepts past times (TASK-5.1); the app does not offer them yet.

### Reference
- TASK-5.1, TASK-5.2 (measurement form), TASK-5.3 (due state follows the occurred time), TASK-5.14 (chart markers).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who measured nitrate two days ago, When they enter the value with that date in the measurement form, Then the entry and the chart show the measurement at that date
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
- API: PATCH of a care entry takes occurredAt; a request with only occurredAt (and optionally a note) moves the time; more than two minutes in the future is refused with care_time_invalid; care.updated in the same transaction.
- App: a When picker (now, yesterday, two days ago, or a typed date and time; never in the future) in the measurement form and the water change quick log (care view and + tab); the edit view corrects the time; local time converts to UTC through the IANA zone. The care plan, Today and the charts follow the chosen time.
- Open decision: the time of an entry from the guided water change mode can be corrected too, which could move a verified water change into another streak period; proposal of the main session: the time of guided entries is fixed, only note, amount and photos stay editable (asked 2026-10-16).
- Verification: see TASK-5.13 (combined state).
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Care/CareTimeTests.cs, CarePlanEndpointsTests.cs, app/client-core/src/occurredAt.test.ts, app/mobile/src/tanks/whenText.test.ts, app/mobile/e2e/playwright/care-when.write.mjs.
- Not verified: the picker on native platforms; the due-care quick log on Today still logs now.

- Decided by the project owner (2026-10-16): the time of an entry from the guided water change mode is fixed; only its note, amount and photos stay editable. Plain entries and measurements stay freely correctable. To implement after TASK-5.7 (same files): the API refuses a time change of a guided entry with a coded problem, the edit view hides the time choice for it.
<!-- SECTION:NOTES:END -->
