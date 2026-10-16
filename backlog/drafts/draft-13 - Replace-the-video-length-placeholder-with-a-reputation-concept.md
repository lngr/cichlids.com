---
id: DRAFT-13
title: Replace the video length placeholder with a reputation concept
status: Draft
assignee: []
created_date: '2026-10-16 05:53'
labels: []
dependencies: []
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The maximum video length per member (TASK-5.10) uses a placeholder rule: accounts older than 1 year with more than 20 public pictures may upload videos up to 3 minutes, everyone else gets the short limit. Work out a reputation concept that fits the product (points and leaderboards are excluded from the app) and replace the placeholder inside the single length policy of TASK-5.10.

### Reference
- TASK-5.10 notes, PROJECT-STATUS O6.
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
