---
id: TASK-5.2
title: Measurements as separate events with value graphs
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-15 20:01'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/measurement.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Care/MeasurementEndpointsTests.cs
parent_task_id: TASK-5
priority: high
ordinal: 82000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Measurements are separate care events (temperature, pH, KH, GH, NO2, NO3, NH4, PO4, conductivity or TDS), entered progressively with units of TASK-4.4 and their origin (test kit, meter, probe); every parameter has a graph over time.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who logs pH and nitrate on three days, When they open the care view, Then they see the values per parameter as a graph over time in their units
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
- A measurement is a care entry of kind measurement with rows in care_measurement_value: parameter (temperature, ph, kh, gh, no2, no3, nh4, po4, conductivity, tds), input value and unit, normalized value in the canonical unit of the parameter, optional origin (test_kit, meter, probe). Units and plausible ranges are checked in the API and as database constraints; hardness converts with 17.848 ppm per German degree.
- The origin is optional, so a quick measurement needs no extra choice; the form offers one origin for all values of an entry.
- API: logging, correcting (PATCH replaces the values) and deleting use the TASK-5.1 routes; GET /api/tanks/{id}/care/series?parameter=&from=&to= returns up to the 1000 newest points oldest first. Owner only, coded problems, outbox payloads carry the values.
- App: Measure next to Water change opens a progressive form (pH, nitrate and temperature first), values in the member's units; one dependency-free line chart per parameter with a summary label and a values list for screen readers.
- Follow-up: the care view loads the series with one request per parameter; a combined series request would save nine round trips.
- Verification: .NET 325/591 (1 opt-in benchmark skipped)/342 with 0 warnings, client-core 49, mobile 254, both typechecks, all 23 write e2e specs green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Care/MeasurementEndpointsTests.cs, CareTransactionTests.cs, app/client-core/src/measurement.test.ts, app/mobile/src/tanks/measurementFormat.test.ts, app/mobile/e2e/playwright/measurement.write.mjs.
- Not verified: native Android and iOS runs, screen reader behaviour, remote CI.
<!-- SECTION:NOTES:END -->
