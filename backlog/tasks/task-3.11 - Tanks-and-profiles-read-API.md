---
id: TASK-3.11
title: Tanks and profiles read API
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-09 01:25'
labels: []
dependencies:
  - TASK-3.4
parent_task_id: TASK-3
priority: high
ordinal: 31000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Tank list and detail with ordered image sections and main image fallback; public profile endpoint with profile and avatar images.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a seeded local stack, When tanks are listed and a tank detail plus its owner profile are fetched, Then the responses cover the legacy tank fields including ordered image lists, main image fallback and public profile data
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Tank list and detail with sections, water values, dimensions and inhabitants; profile endpoint with public stats and preset picture and tank lists. Real-stack smoke verified.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
