---
id: TASK-3.41
title: Keep members signed in per device until they sign out
status: In Progress
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-13 08:41'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
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
