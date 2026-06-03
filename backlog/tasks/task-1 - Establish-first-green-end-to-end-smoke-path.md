---
id: TASK-1
title: Establish first green end-to-end smoke path
status: To Do
assignee: []
created_date: '2026-06-03 08:08'
updated_date: '2026-06-03 16:02'
labels:
  - story
  - e2e
  - smoke
milestone: m-0
dependencies:
  - TASK-2.1
  - TASK-2.8
references:
  - app/mobile/e2e/maestro/smoke.yaml
priority: high
ordinal: 1000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The one user-observable vertical slice of phase 1: a trivial but real end-to-end path proving the test-bound Definition-of-Done pipeline works before feature work begins. A minimal ASP.NET Core (.NET 10) API exposes a health endpoint with its OpenAPI document (Scalar reference UI); a TypeScript client is generated from that OpenAPI document into the shared client core; a minimal Expo / React Native screen calls the health endpoint and renders the status. A Maestro flow tagged with this story id drives the bundled native Android app on a KVM-accelerated emulator and the same suite runs against the web channel (ADR-0004/0006), asserting the screen shows healthy. The app and API are deployed through the full GitOps pipeline (preview/staging) and the E2E runs against the deployed stack.

Built red-green (ADR-0003): the failing Maestro flow and the failing API contract test are written first. Done means the E2E is green on Android and web, unit/integration tests cover the contract, CI is fully green, and the story is merged to main with the bidirectional story-test binding intact (ADR-0015). No authentication is involved (health path); Keycloak login is phase 2.

Verifying artifact: app/mobile/e2e/maestro/smoke.yaml carrying the back-reference tag task-1.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the deployed stack, When the Maestro smoke flow runs the user path on the Android emulator, Then the app screen shows the API health status and the flow passes green
- [ ] #2 Given the same Maestro suite, When it runs against the web channel, Then it also passes green
- [ ] #3 Given the API, When its health contract test runs, Then it passes and the OpenAPI document is generated
- [ ] #4 Given the generated TypeScript client, When the client-core typecheck/lint/unit job runs, Then it passes
- [ ] #5 Given the story marked Done, When the story-test-binding CI gate runs, Then app/mobile/e2e/maestro/smoke.yaml exists and carries the task-1 back-reference
<!-- AC:END -->



## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
