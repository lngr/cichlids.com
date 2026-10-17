---
id: TASK-7
title: Provisional app accounts
status: In Review
assignee: []
created_date: '2026-10-16 20:27'
updated_date: '2026-10-17 07:44'
labels: []
dependencies: []
priority: high
ordinal: 110000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Installing the app gives a working account without a form: the installation holds a key pair, the API knows its public key and issues short-lived device tokens, everything a provisional account creates stays private, and registering keeps or moves all of it (ADR-0030, accepted by the project owner, 2026-10-16).

### Decisions of the project owner (2026-10-16)
- Provisional accounts may create their own tanks, log care and use the media library; all of it stays private until registration.
- Following, comments, ratings, kudos, stories, messages and every other action others see need registration; following leads to registration.
- Signing in with an existing Keycloak account asks whether to take the provisional content along; on yes it moves with a device signature and the Keycloak token in the same request.
- A provisional account without a signed request for 12 months is deleted with its content.
- The web has no provisional accounts.

### Order
TASK-7.1 device keys and tokens, TASK-7.2 rights of provisional accounts, TASK-7.3 registration and taking content along, TASK-7.4 cleanup, TASK-7.5 app flow (alongside 7.2 and 7.3).

### Reference
- ADR-0030, ADR-0023, ADR-0027, DRAFT-12 (web funnel part stays a draft).
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
