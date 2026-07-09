---
id: TASK-3.19
title: Mobile app gallery and picture detail
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-09 03:34'
labels: []
dependencies:
  - TASK-3.18
parent_task_id: TASK-3
priority: high
ordinal: 39000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Expo app shell with the design system tokens; gallery listing and picture detail screens over the generated client.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the seeded local stack, When the Expo app opens the gallery and a picture detail, Then migrated pictures render with variants, title, user and comments, verified by a Maestro flow
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Expo app with expo-router, token-derived light and dark theme, gallery with infinite grid plus sort and topic filters, picture detail with paginated comments. Verified on web against real data with Playwright smoke and screenshots; Maestro flows checked in, runner blocked in this environment (device attach issue, documented).
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
