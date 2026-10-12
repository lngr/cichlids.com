---
id: TASK-2.27
title: Separate the Keycloak realm configuration for staging and production
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-2
priority: high
ordinal: 56000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R7, R33.

The dev realm file stays and is marked as local only. Staging and production get their own realm configuration: HTTPS required, registration opened only after the account import (ADR-0023), no password grant on the public client, exact redirect URIs per environment, no test accounts. A CI check fails when a dev setting appears in that configuration. Outside Development the API refuses to start without an HTTPS authority and an audience.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the staging realm configuration, When the operator imports it into a fresh Keycloak, Then HTTPS is required, registration is closed, the public client has no password grant and no test account exists
- [ ] #2 Given a commit that copies a dev setting into the staging configuration, When GitHub Actions runs, Then the check fails
- [ ] #3 Given the API in the Staging environment without Authentication:Authority, When the operator starts it, Then it refuses to start and names the setting
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
