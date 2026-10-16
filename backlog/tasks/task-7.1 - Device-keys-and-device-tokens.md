---
id: TASK-7.1
title: Device keys and device tokens
status: To Do
assignee: []
created_date: '2026-10-16 20:27'
labels: []
dependencies: []
parent_task_id: TASK-7
priority: high
ordinal: 111000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The app registers the public key of its installation and gets short-lived device tokens for the provisional profile by signing a one-time challenge (ADR-0030).

### Scope
- Registration: public key (ECDSA P-256) in, provisional profile with a generated name and no public handle plus a device credential out; rate limited per network address.
- Challenge and token: fresh challenge, accepted once and within minutes; signature over challenge, credential id and timestamp; API-issued access token (own issuer and audience, 15 minutes, no refresh) with the provisional profile as subject and a provisional claim; signing key from the secret store like the session key ring, with rotation.
- The API accepts Keycloak tokens and device tokens side by side; one check tells every endpoint whether the caller is registered.
- Credential state: active or retired; the last signed request time is kept for the cleanup.

### Reference
- ADR-0030, ADR-0027.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a fresh installation, When the app registers its public key and signs a challenge, Then it receives a device token for a new provisional profile
- [ ] #2 Given a used or expired challenge or a wrong signature, When the app asks for a token, Then the API refuses it
- [ ] #3 Given a device token, When it is older than 15 minutes, Then the API refuses it and a new signed challenge yields a new one
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
