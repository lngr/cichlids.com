---
id: TASK-7.1
title: Device keys and device tokens
status: In Review
assignee: []
created_date: '2026-10-16 20:27'
updated_date: '2026-10-16 20:56'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/device-token.write.mjs
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation (2026-10-16)
- profile kind provisional; device_credential (SPKI public key, ES256, active or retired, last signed request); device_challenge (hash of a single-use value, 2 minutes).
- POST /api/auth/device/credentials, /challenges, /tokens (anonymous, per-address rate limits in memory); message cichlids-device-auth-v1, challenge, credential id, unix seconds; P1363 signatures; the challenge row is deleted before checking, so each challenge allows one attempt; every refusal answers 401 device_auth_refused.
- Device tokens: ES256, own issuer and audience, 15 minutes, kid from a key set (tools/create-device-token-key.cs, DeviceTokens__KeySetFile; the API refuses to start outside Development without it); each request checks the credential is active.
- CurrentProfileService.ResolveAsync refuses provisional callers with 403 registration_required; /api/me reports provisional; Keycloak-only code reads the subject through KeycloakSubject().
- Interpretations: provisional username placeholder provisional-<guid>, display name user plus eight digits; registering the same key twice returns the same credential.
- Verification (branch): .NET 643/2194 (1 skipped)/350, client-core 303, mobile 451, 49 write e2e specs green.
<!-- SECTION:NOTES:END -->
