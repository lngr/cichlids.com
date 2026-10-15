---
id: TASK-4.1
title: Feature flags in API and app
status: To Do
assignee: []
created_date: '2026-10-15 08:46'
labels: []
dependencies: []
parent_task_id: TASK-4
priority: high
ordinal: 72000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Features that only make sense with enough activity ship finished but switched off. Flags live in
the API configuration per environment and reach the app through an endpoint; the app hides
flagged sections while a flag is off.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the flag discover-activity switched off in an environment, When a member opens Discover, Then the activity sections are absent, and after switching the flag on they appear without a new app build
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
