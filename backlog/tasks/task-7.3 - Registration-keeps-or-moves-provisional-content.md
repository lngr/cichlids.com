---
id: TASK-7.3
title: Registration keeps or moves provisional content
status: To Do
assignee: []
created_date: '2026-10-16 20:28'
labels: []
dependencies:
  - TASK-7.1
parent_task_id: TASK-7
priority: high
ordinal: 113000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Registering or signing in from the app while the device token is valid keeps everything (ADR-0030).

### Scope
- New Keycloak account: the API links the Keycloak subject to the provisional profile, which becomes a full profile with its first public handle; the device credential is retired and the session continues as a Keycloak session.
- Existing Keycloak account with a profile: the app asks whether to take the provisional content along; on yes the API moves tanks, care data, streak data and media into that profile in one transaction and deletes the provisional profile; the request carries a fresh device signature and the Keycloak token, and refuses either alone.

### Reference
- ADR-0030, ADR-0023, ADR-0027.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a provisional account with a tank, When the member registers a new account in the app, Then the tank and its care entries belong to the registered profile
- [ ] #2 Given a provisional account with a tank and an existing account, When the member signs in and agrees to take the content along, Then the tank belongs to the existing profile and the provisional profile is gone
- [ ] #3 Given only a device signature or only a Keycloak token, When the move is requested, Then the API refuses it
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
