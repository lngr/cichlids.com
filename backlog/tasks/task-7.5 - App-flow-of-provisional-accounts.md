---
id: TASK-7.5
title: App flow of provisional accounts
status: In Review
assignee: []
created_date: '2026-10-16 20:28'
updated_date: '2026-10-17 07:44'
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation (2026-10-17)
- Native device account: ECDSA P-256 private key in expo-secure-store (Keychain AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY, Keystore-encrypted storage on Android), signatures through @noble/curves 2.2.0 since the Expo runtime has no ECDSA primitive; the key is loaded into memory for one signature. A key that never leaves the secure hardware needs a native module (DRAFT-18).
- First start registers in the background with staggered retries; an offline first start never blocks the app.
- Device tokens are sent only while no account session exists; a 401 drops the cached token, a refused credential ends the device account; a 403 registration_required on a write opens the registration with its reason.
- Note about the device account before the first tank; gated actions (follow, comment, rate, stories, publish, visibility, profile images, audience) open the registration sheet with the reason.
- After sign-in with an existing account: the question whether to take the content along, with the counts of the preview; on no the device account stays hidden on the device until sign-out or a later move.
- Web: no device account; the write E2E build contains a WebCrypto test key provider (non-extractable key in IndexedDB) behind EXPO_PUBLIC_DEVICE_TEST_KEYS plus a browser opt-in; production and staging builds with the flag fail.
- Verification on main: .NET 643/2392 (1 skipped)/350, client-core and mobile unit green (mobile 520), 53 write E2E specs green including provisional-app. Android and iOS are not run on a device.
<!-- SECTION:NOTES:END -->
