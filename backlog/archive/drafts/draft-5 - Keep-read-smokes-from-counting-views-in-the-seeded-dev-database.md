---
id: DRAFT-5
title: Keep read smokes from counting views in the seeded dev database
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
Read-only smokes and local browsing leave the seeded dev database unchanged.

### Context
- GET /api/pictures/{slug} increments view_count (legacy parity), so the Playwright web smoke and every opened picture detail write into the seeded dev database.
- The app refetches the picture detail after a comment post, which counts one more view.

### Options
- A configuration switch that disables view counting for the local dev API.
- A separate endpoint or query flag for refetches that do not count a view.
- Running the read smoke against a copy of the seeded database.
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
