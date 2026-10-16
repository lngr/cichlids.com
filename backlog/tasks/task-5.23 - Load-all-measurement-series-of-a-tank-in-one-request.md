---
id: TASK-5.23
title: Load all measurement series of a tank in one request
status: In Review
assignee: []
created_date: '2026-10-16 15:48'
updated_date: '2026-10-16 15:56'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/measurement-series.write.mjs
parent_task_id: TASK-5
priority: low
ordinal: 109000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The measurement charts of a tank load the series of all parameters with one request; each parameter has its own request (TASK-5.2), which costs ten round trips on a phone connection. Visibility and only-me rules of the care stream apply as for the single series.

### Reference
- TASK-5.2, TASK-5.7, TASK-5.14.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a tank with measurements of several parameters, When the member opens the care view, Then the charts appear after one series request and show the same values as before
- [ ] #2 Given a visitor who may see the care stream, When they open the charts, Then only-me measurements are left out as in the single series
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
- API: GET /api/tanks/{id}/care/series/all (optional from and to) returns every parameter with points, up to 1000 newest points each, with the access rules of the single series; the single-parameter route stays for direct readers.
- Client: care.allSeries; MeasurementGraphs makes one call.
- Verification: .NET 559/1351 (1 skipped)/349, client-core 253, mobile 430, typechecks; measurement-series, measurement, water-change-marks, care-heatmap and care specs green.
<!-- SECTION:NOTES:END -->
