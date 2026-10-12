---
id: DRAFT-4
title: Decide how often a member can rate the same picture or tank
status: Draft
assignee: []
created_date: '2026-09-28 15:42'
labels: []
dependencies: []
parent_task_id: TASK-3
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A product rule for repeated ratings by the same member on the same target.

### Context
- The comment API accepts any number of ratings per member and target; each one enters rating_average and rating_count.
- The app composer makes repeated ratings a one-tap action.
- Options: one rating per member and target with overwrite, reject a second rating, or keep the current behaviour (legacy parity to be checked against the migrated data).

### Scope
- Decision, then a unique index or upsert in the comment write service and the matching API tests.
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
