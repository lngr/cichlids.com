---
id: TASK-3.4
title: ETL migrates tanks with inhabitants
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-08 22:49'
labels: []
dependencies:
  - TASK-3.2
  - TASK-3.3
parent_task_id: TASK-3
priority: high
ordinal: 24000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Tanks with owner, category, dimensions and texts; fish and fish_count parallel lists resolved into inhabitant rows referencing species.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given migrated profiles and species, When the tank ETL step runs twice, Then every non-filtered legacy tank exists exactly once with owner, category, dimensions and texts, and its fish lists are resolved into inhabitant rows with species references
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Real double run: 4990 tanks, inhabitants resolved from fish/fish_count parallel lists, 21 tanks with category 0 mapped to null, 130 placeholder profiles for hard-deleted owners. Idempotent, covered by container tests.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
