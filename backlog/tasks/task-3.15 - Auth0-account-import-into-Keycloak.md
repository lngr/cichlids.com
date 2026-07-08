---
id: TASK-3.15
title: Auth0 account import into Keycloak
status: To Do
assignee: []
created_date: '2026-07-08 18:58'
labels: []
dependencies:
  - TASK-3.13
  - TASK-3.3
parent_task_id: TASK-3
priority: medium
ordinal: 35000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Import of the Auth0 export into Keycloak: linking to profiles via sub and email join, duplicate email consolidation per the agreed merge rules, reset on first login for password users, social identities prepared.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the Auth0 export and migrated profiles, When the import runs, Then every active account exists in Keycloak linked to its profile, duplicate emails are consolidated per the agreed merge rules, and password users require a reset on first login
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
