---
id: TASK-7.3
title: Registration keeps or moves provisional content
status: In Review
assignee: []
created_date: '2026-10-16 20:28'
updated_date: '2026-10-16 21:57'
labels: []
dependencies:
  - TASK-7.1
references:
  - app/mobile/e2e/playwright/provisional-registration.write.mjs
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation (2026-10-16)
- POST /api/me/provisional/preview, /upgrade, /move (registered-only; Keycloak bearer plus a signed device challenge in the body); upgrade turns the provisional profile into a member with a handle from the shared member naming and links the oidc identity; move transfers tanks with care, streak and plan data, to-dos, snoozes, sessions, uploads, collections, used checksums and media into the existing member profile in one transaction and removes the provisional profile through the shared removal; all device credentials are retired.
- Locking in the order of the cleanup; races end with one winner (409 account_has_profile, account_has_no_profile; 403 account_suspended, device_auth_refused).
- Vacations: overlapping ones and ones that would repair a period of the member's tanks and start more than 7 days before the move are dropped and counted (vacationsDropped, vacationsDroppedRepair); preview and move take the local date.
- Events profile.registered and profile.provisional_content_moved with ids and counts.
- Verification (branch): .NET 643/2391 (1 skipped)/350, client-core 311, mobile 451, the new spec green; full suite on main follows.
<!-- SECTION:NOTES:END -->
