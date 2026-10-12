---
id: TASK-3.33
title: Harden the account import and the identity handling of the ETL
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: high
ordinal: 55000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R2, R31 (import part), R36.

The profile step replaces only identities of the legacy providers auth0 and legacy-openid. The account import links logins to member profiles only. The Keycloak admin client keeps response bodies out of its errors, has no admin/admin fallback outside Development, and retries 429 and 5xx with a growing delay.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an oidc identity on a migrated profile, When the profile step runs again on the dev stack, Then the oidc identity still exists
- [ ] #2 Given an archived placeholder profile with a legacy id, When the account import runs, Then no login is linked to it
- [ ] #3 Given a Keycloak error during the import, When the operator reads the error output, Then it contains no username and no email address
- [ ] #4 Given no admin credentials configured outside Development, When the operator starts the import, Then it aborts with a message naming the missing setting
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
