---
id: TASK-1
title: Establish first green end-to-end smoke path
status: To Do
assignee: []
created_date: '2026-06-03 08:08'
labels: []
dependencies: []
priority: high
ordinal: 1000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Bootstrap the minimal stack so one user-visible path runs green end-to-end, proving the test-bound Definition of Done pipeline works before feature work begins.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the freshly started stack, When the end-to-end smoke test runs the user path, Then it passes green in CI
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
