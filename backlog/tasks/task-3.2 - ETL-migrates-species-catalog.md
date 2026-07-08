---
id: TASK-3.2
title: ETL migrates species catalog
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-08 20:29'
labels: []
dependencies:
  - TASK-3.1
parent_task_id: TASK-3
priority: high
ordinal: 22000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Species, genus and name lookups, common names and care data from user_cichlids_species and its lookup tables, idempotent upsert on legacy_id.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the restored legacy MySQL and a migrated schema, When the species ETL step runs twice, Then 829 species with genus and name data exist exactly once and source and target counts match
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Idempotent upsert on legacy_id; real double run: 829 species, 825 slugs (4 without legacy alias), enums mapped, common names and links delete-and-insert under parent. Covered by Etl.Tests against mysql:5.7 plus postgres:17 containers.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
