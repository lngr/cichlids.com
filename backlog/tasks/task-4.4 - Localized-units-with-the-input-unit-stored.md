---
id: TASK-4.4
title: Localized units with the input unit stored
status: In Review
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 09:55'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/01-units.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Tanks/TankUnitsTests.cs
  - app/mobile/src/units/format.test.ts
parent_task_id: TASK-4
priority: medium
ordinal: 75000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Units follow the device country (metric or US) and can be changed separately from the language;
the API stores metric values plus the unit the member entered, so a 55 gallon tank always shows
as 55 gallons.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member in the US who creates a 55 gallon tank, When a member with metric units and later the creator open it, Then the metric member sees the liters and the creator sees exactly 55 gallons
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
### Implemented
- Units preference metric or US, default from the device region, changeable in Me separately from
  the language, kept per device.
- API: tank dimensions and volume stored metric plus the input value and unit (migration
  `TankMetricAndInputMeasurements`, ETL maps legacy values); responses carry both; new
  `POST /api/tanks` through `TanksWriteService` with a `tank.created` outbox event in the same
  transaction and problem details codes `tank_title_invalid`, `tank_dimensions_invalid`,
  `tank_volume_invalid` (translated in all catalogs).
- App: values shown in the preference of the viewer, the owner sees the input value; exact
  conversion factors with sources.

### Verified by the agents (Codex) and the main session
- Red first (405, missing original input, missing liter display, missing event and codes);
  .NET Api 457, Infrastructure 324, Etl 342; client-core 22; mobile 238; write suite 17 of 17
  including `01-units.write.mjs` (55 US gal for the owner, 208.2 L for another member).
<!-- SECTION:NOTES:END -->
