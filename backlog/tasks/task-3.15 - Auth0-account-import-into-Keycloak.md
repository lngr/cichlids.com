---
id: TASK-3.15
title: Auth0 account import into Keycloak
status: To Do
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-09 09:46'
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
### Goal
Import legacy accounts into Keycloak so returning members keep their identity, sized for the win-back campaign.

### Scope
- Import every legacy account with a known email address (Auth0 export joined with fe_users emails, deduplicated), not only accounts with a migrated profile.
- Accounts with a migrated profile are linked to it (oidc identity); accounts without a profile get one on first login through the existing /api/me flow.
- Password users get the required action update password (the export has no hashes); all imported accounts get the required action terms and conditions, so the first login runs through the campaign gate.
- Accounts without any known contact data and without a profile are not imported.
- Social identities (Google, Facebook) are prepared as identity provider links.
- The import is repeatable (idempotent on email).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the Auth0 export and migrated profiles, When the import runs, Then every active account exists in Keycloak linked to its profile, duplicate emails are consolidated per the agreed merge rules, and password users require a reset on first login
- [ ] #2 Given the Auth0 export and the legacy user table, When the import runs twice, Then every account with a known email exists exactly once in Keycloak with the agreed required actions, profile-linked where a profile exists, and accounts without contact data and without profile are absent
<!-- AC:END -->



## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
