---
id: TASK-3.41
title: Keep members signed in per device until they sign out
status: In Progress
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-13 10:03'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/session.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Auth/WebSessionTests.cs
  - app/api/tests/Cichlids.Api.Tests/Features/Auth/KeycloakOidcClientTests.cs
  - app/api/tests/Cichlids.Etl.Tests/KeycloakAccountImportTests.cs
  - app/mobile/src/auth/tokenRefresh.test.ts
  - app/mobile/src/auth/webSession.test.ts
  - app/mobile/src/upload/savedUploadInput.test.ts
  - docs/adr/0027-session-model-for-native-and-web-clients.md
parent_task_id: TASK-3
priority: medium
ordinal: 65000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R8.

A member signs in once per device and stays signed in until signing out; no sign-out after inactivity or on errors. Keycloak offline tokens without a practical expiry back the sessions; the native app stores them in SecureStore; network and server errors keep the tokens. Suspended or deleted accounts lose their sessions. The web app keeps its session in an httpOnly cookie managed by the API (backend for frontend) with CSRF protection; an ADR records the session model. Upload form input survives a sign-out.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member signed in on a phone, When the phone stays unused for three months, Then the app opens signed in
- [ ] #2 Given the phone in flight mode while the access token expires, When the member opens a screen, Then the app shows a connection message and stays signed in
- [ ] #3 Given the web app, When the operator inspects the browser storage and the script-visible cookies, Then no token is visible
- [ ] #4 Given a suspended account, When its member opens the app, Then the member is signed out
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach (test-first)
- Red first at each layer: mobile unit tests for the token store and refresh outcomes, Api.Tests
  for the session endpoints, CSRF and cookie flags, then Playwright write specs.

### Native app
- Login requests the `offline_access` scope; Keycloak realm files (dev, staging, production) give
  offline sessions no practical expiry and keep the CI realm check green.
- Tokens live in SecureStore; a refresh that fails with network or server errors keeps the tokens
  and the call fails with a clear connection message; only `invalid_grant` (revoked, suspended or
  deleted account) signs the member out.
- The upload form input (photo, title, description) is stored locally and survives a sign-out.

### Web app (backend for frontend)
- The API manages the web session: login start and callback (authorization code with PKCE on the
  server), logout and a session endpoint; tokens stay on the server, the browser holds only an
  httpOnly, Secure, SameSite cookie; a CSRF token protects state-changing requests; the API
  accepts either the bearer token (native) or the session cookie (web).
- Same-site deployment of web app and API is the default; development works on localhost.
- An ADR records the session model (native offline tokens, web BFF cookie, CSRF, revocation by
  suspension).

### Tests and references
- Unit tests for refresh outcomes and storage; Api.Tests for the BFF flow, cookie flags and CSRF;
  Playwright write specs tagged TASK-3.41: the web storage holds no token (AC #3), a suspended
  account is signed out (AC #4), and an offline refresh keeps the session (AC #2 via network
  interception). AC #1 (three months) is covered by the realm settings and a unit test of the
  refresh path.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- ADR-0027 "Session model for native and web clients" (Proposed).
- Keycloak (dev, staging, production): offline sessions idle ten years, no max lifespan,
  `offline_access` scope; realm check rules with self-test. The account import grants
  `default-roles-cichlids`, without it imported accounts could not sign in with offline tokens.
- Native: own refresh call; 400 and 401 end the session, every other failure keeps the tokens and
  raises `SessionUnavailableError` (text `errors.connection`); a refresh at start and on return to
  the foreground (at most every 5 minutes) enforces suspensions.
- Web BFF: authorization code with PKCE on the server, tokens in table `web_session` (only the
  SHA-256 of the session id stored, migration `AddWebSession`); cookie `__Host-cichlids-session`
  (development over http `cichlids-session`), httpOnly, Secure, SameSite=Lax, 400 days, renewed on
  app start; CSRF header `X-CSRF-Token` plus origin check; policy scheme: bearer first, else
  cookie; refused refresh deletes the session, unreachable Keycloak answers 503
  `identity_provider_unavailable`. Login start is a POST answered with JSON, so an unreachable
  login service keeps the message with retry of TASK-3.40.
- `prompt=login` on native and web login, so a shared device never signs the next person in
  through the browser SSO session.
- Upload input (title, description, topic, unfinished photo) stored per draft and account
  (SecureStore in chunks natively, localStorage on the web), restored when the draft is opened.

### Verified by the agent
- `dotnet test`: Infrastructure 304, Api 385, Etl 341; client-core 13, mobile 173, typecheck;
  realm check (53 cases); ADR index; `pnpm run e2e:write` 11 of 11 including `session.write.mjs`
  (no script-visible token, signed in after three months, connection message, upload input after
  sign-out, suspension signs out).
- Not verified: native app on a device or emulator; Keycloak outage only in Api.Tests.

### Open questions for the project owner
- Encryption at rest of the tokens in `web_session`.
- Public client `cichlids-app` with PKCE for the BFF, or a confidential client with secret.
- `prompt=login` only, or additionally a front-channel logout of the browser SSO session.
- Same-origin routing of web app and API for staging and production depends on the open web scope.

### Follow-ups
- Cleanup of orphaned `web_session` rows and Keycloak offline sessions.
- A failed web login (refused or code exchange failed) returns without a message.

### Follow-ups done
- `WebSessionCleanupService` deletes `web_session` rows unused for longer than the retention
  (`WebSessionCleanup`: Enabled true, Interval 1 day, Retention 400 days, BatchSize 100) after
  revoking the Keycloak offline session; without an answer from Keycloak the row stays for the next
  pass. `last_seen_at` is renewed with the cookie.
- A failed web login returns with `sign_in=failed`; the app shows "Login failed. Please try again."
  with "Try again" (English and German); `session.write.mjs` asserts it.
- Known gap: a logout while Keycloak is unreachable leaves the offline session in Keycloak without
  a row to retry it.

### Decided by the project owner
- The tokens in `web_session` are stored encrypted (ASP.NET Data Protection, key ring outside the
  database as a SOPS-managed cluster secret, a development key locally). To implement.
<!-- SECTION:NOTES:END -->
