---
id: DRAFT-6
title: Let members change their public handle
status: Draft
assignee: []
created_date: '2026-09-28 18:52'
labels: []
dependencies: []
parent_task_id: TASK-3
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A member changes the public handle (profile.username) in the app.

### Context
- The identity model separates the account (Keycloak user), its logins (password with login username or email, Google, Facebook, passkeys) and the public handle (ADR-0023).
- Migrated members without a usable legacy name and new members get a generated handle user<number>.

### Scope
- API endpoint to change the handle with rules: allowed characters, length, uniqueness, never an email address; rate limit.
- Legacy redirects and profile links keep working (profiles resolve by id).
- Me tab: edit the handle.
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
