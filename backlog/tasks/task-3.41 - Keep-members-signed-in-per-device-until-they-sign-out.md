---
id: TASK-3.41
title: Keep members signed in per device until they sign out
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
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
