---
id: TASK-5.1
title: Care event stream with one-tap water change
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-15 19:08'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/care.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Care/CareEndpointsTests.cs
parent_task_id: TASK-5
priority: high
ordinal: 81000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Care entries form an event stream per tank. A water change is logged with one tap on the tank page and from the + tab; the amount (liters, gallons or percent) can be added later. Every entry appears in the care view and the timeline of the tank.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member with a tank, When they tap Water change on the tank page, Then a water change entry with the current time exists in the care view of that tank and can be completed with an amount afterwards
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
- Care entries form an event stream per tank (table care_entry: kind water_change, occurred at, recorded at, optional amount with input value and unit liter, us_gallon or percent and a normalized liter value; check constraints and an index by tank and time).
- API under /api/tanks/{tankId}/care: log (now by default, past times allowed, at most two minutes in the future), correct or clear the amount, delete, list newest first with paging. Until visibility per entry exists (TASK-5.7) only the tank owner reads its care entries; others get a coded 404. Outbox events care.logged, care.updated and care.deleted in the same transaction; framework refusals on exactly these routes carry a care problem code.
- App: one-tap Water change in the care view of the own tank and from the + tab with a tank choice, calm confirmation with undo (undo deletes the entry and survives a connection failure with retry), amount editing in the member's units.
- Verification: .NET 325/539 (1 opt-in benchmark skipped)/342 with 0 warnings, client-core 34, mobile 246, both typechecks, all 22 write e2e specs green; the care spec ran five times in a row green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Care/CareEndpointsTests.cs, CareTransactionTests.cs, app/client-core/src/care.test.ts, careClient.test.ts, app/mobile/src/tanks/careAmount.test.ts, app/mobile/e2e/playwright/care.write.mjs.
- Not verified: native Android and iOS runs, remote CI.
<!-- SECTION:NOTES:END -->
