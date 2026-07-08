---
id: TASK-3.21
title: Mobile app login and photo upload
status: To Do
assignee: []
created_date: '2026-07-08 18:58'
labels: []
dependencies:
  - TASK-3.19
  - TASK-3.16
parent_task_id: TASK-3
priority: medium
ordinal: 41000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Keycloak login in the app and the photo upload plus publish flow.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the local stack, When a user logs in via Keycloak in the app and uploads a photo, Then the published photo appears in the gallery, verified by a Maestro flow
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
