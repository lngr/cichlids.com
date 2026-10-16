---
id: TASK-5.17
title: Care heatmap over 16 weeks
status: In Review
assignee: []
created_date: '2026-10-16 09:22'
updated_date: '2026-10-16 09:39'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/care-heatmap.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Care/CareHeatmapEndpointsTests.cs
parent_task_id: TASK-5
priority: medium
ordinal: 97000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A calendar heatmap (contribution graph style) shows on which days the member cared for a tank over the last 16 weeks: water changes and measurements, independent of the streaks (decided by the project owner, 2026-10-16). Shown on the tank page and, summed over all tanks, on Today or Me; visibility follows the care stream (TASK-5.7).

### Reference
- TASK-5.5 notes, DRAFT-11 (target Today), PRODUCT-VISION epic 2.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who logged care on several days of the last 16 weeks, When they open the tank page, Then the heatmap marks exactly those days
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
- API: GET /api/tanks/{id}/care/heatmap and GET /api/me/care/heatmap (own tanks summed); 16 ISO weeks ending today in the client's local date; one grouped query counts water changes and measurements per local day; owner sees everything, others need the care stream and never count only-me entries (coded 404 otherwise); the existing care_entry index serves the query.
- App: CareHeatmapView (16 week columns by 7 days, accent opacity by count, a dot for measurement days, legend) in the care view and on Today below the streaks; a screen reader summary and a 48dp toggle to a newest-first list of care days; light and dark.
- Verification: .NET 553/1200 (1 opt-in benchmark skipped)/349 with 0 warnings, client-core 232, mobile 401, both typechecks, all 36 write e2e specs green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Care/CareHeatmapTests.cs, CareHeatmapEndpointsTests.cs, app/client-core/src/careHeatmap.test.ts, app/mobile/src/careHeatmap/heatmapText.test.ts, app/mobile/e2e/playwright/care-heatmap.write.mjs.
- Not verified: native rendering; the Today heatmap refreshes on focus, not right after a quick log on Today.
<!-- SECTION:NOTES:END -->
