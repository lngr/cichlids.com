---
id: TASK-7.5
title: App flow of provisional accounts
status: To Do
assignee: []
created_date: '2026-10-16 20:28'
labels: []
dependencies:
  - TASK-7.1
parent_task_id: TASK-7
priority: high
ordinal: 115000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The app works from the first start without a form and leads to registration where it matters (ADR-0030).

### Scope
- First start: create the key pair in the secure store (Secure Enclave or Keychain, hardware-backed Keystore where available), register, keep tokens fresh by signing.
- Before the first tank: a short note that the account lives on this device until registration, with the offer to register.
- Gated actions (follow, comment, publish, make public) open the registration with the reason.
- After sign-in with an existing account: the question whether to take the content along.
- The web build has no provisional accounts; for the write E2E stack a test key provider backed by WebCrypto non-extractable keys may stand in for the secure store, enabled only there.

### Reference
- ADR-0030, TASK-7.2, TASK-7.3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a first start, When the member creates a tank without signing up, Then the tank is there and private, and the app showed the note about the device account
- [ ] #2 Given a provisional account, When the member taps follow, Then the app opens the registration with the reason
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
