---
id: TASK-2.28
title: Run the application test suites in CI
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-2
priority: high
ordinal: 59000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R6.

CI gets three application jobs: backend (.NET build and all tests with Testcontainers), frontend (typecheck and vitest for client-core and mobile) and the Playwright write tests against the compose stack without legacy data. The seeded smoke follows with TASK-2.24, the Maestro flows with TASK-2.25. These jobs are the backend, client-core and e2e checks named in TASK-2.11.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a push to main, When GitHub Actions runs, Then the backend, frontend and e2e write jobs run and the operator sees them green
- [ ] #2 Given a commit that breaks an API integration test, When GitHub Actions runs, Then the backend job fails
- [ ] #3 Given a commit that breaks the upload user path, When GitHub Actions runs, Then the e2e job fails
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
