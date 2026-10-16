---
id: DRAFT-18
title: Hardware-bound device keys in the app
status: Draft
assignee: []
created_date: '2026-10-16 21:57'
labels: []
dependencies: []
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The device key of a provisional account lives non-exportable in the Secure Enclave on iOS and in the hardware-backed Keystore (StrongBox where available) on Android, as ADR-0030 describes. The app build of TASK-7.5 keeps the key in the operating system's secure storage through a crypto library, because this needs a native module that can only be built and tested on devices or emulators.

### Scope
- An Expo native module (or a maintained library) for key generation and ECDSA P-256 signing in hardware, behind the key provider interface of client-core.
- Fallback to the secure storage provider on devices without hardware support.
- Migration of existing provisional accounts: the app registers a new hardware key for the same provisional profile, signed with the old key, and retires the old credential.

### Reference
- ADR-0030, TASK-7.1, TASK-7.5.
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
