---
id: TASK-3.13
title: Local Keycloak with OIDC login
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-09 02:33'
labels: []
dependencies: []
parent_task_id: TASK-3
priority: high
ordinal: 33000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Declarative realm for the local stack; API validates Keycloak JWTs and resolves the profile; registration and login work locally.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the local stack with the Keycloak realm applied, When a user obtains a token and calls an authenticated endpoint, Then the API accepts the JWT and resolves the profile, and unauthenticated calls to protected endpoints are rejected
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Declarative realm (clients cichlids-api/cichlids-app, roles moderator/admin, dev users), JWT bearer auth with role mapping, profile resolution via oidc subject with email fallback and auto-creation, /api/me. Real smoke with password-grant tokens against the stack Keycloak.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
