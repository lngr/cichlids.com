---
id: TASK-2.25
title: Run the Maestro flows of the native app in CI
status: To Do
assignee: []
created_date: '2026-10-09 21:28'
labels: []
milestone: m-0
dependencies: []
parent_task_id: TASK-2
priority: medium
ordinal: 50000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Maestro flows in app/mobile/e2e/maestro (browse-gallery, tank-detail, community-thread, login-upload, comment-rating) are written but have never run: the runner blocks on device attach in the devcontainer. ADR-0004 and ADR-0006 name Maestro as the end-to-end tool for the native app. This task makes the flows run on an Android emulator in CI and fixes the flow defects listed under R42 in REVIEW-MVP.md (German UI texts asserted while the app defaults to English, a final assertion that matches any screen, a hardcoded appId).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a push to main, When GitHub Actions runs, Then a job starts an Android emulator, runs all Maestro flows against the stack, and the operator sees them pass in the Actions log
- [ ] #2 Given the emulator locale set to English and then to German, When the flows run, Then they pass in both locales
- [ ] #3 Given a commit that removes the picture detail screen, When the browse-gallery flow runs, Then it fails
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
