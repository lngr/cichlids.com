---
id: TASK-2.24
title: Run the Playwright web smoke against anonymized seed data in CI
status: To Do
assignee: []
created_date: '2026-10-09 21:28'
labels: []
milestone: m-0
dependencies:
  - TASK-2.17
parent_task_id: TASK-2
priority: medium
ordinal: 49000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Playwright web smoke (app/mobile, e2e:web) browses gallery, picture detail, tanks and community against a seeded stack. It runs locally against the migrated legacy data, which cannot enter CI (size, personal data). Once TASK-2.17 provides an anonymized snapshot, CI seeds a stack from it and runs the smoke on every push, so the read paths are part of the green gate (ADR-0003, ADR-0005).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a push to main, When GitHub Actions runs, Then a job seeds the stack from the anonymized snapshot and runs the Playwright web smoke, and the operator sees the job green in the Actions log
- [ ] #2 Given a commit that breaks the gallery read path, When GitHub Actions runs, Then the smoke job fails and the run is red
- [ ] #3 Given the CI job definition, When the operator inspects which data it loads, Then no migrated legacy data and no personal data is used
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
