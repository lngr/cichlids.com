---
id: TASK-3.42
title: Apply the code and documentation corrections from the MVP review
status: To Do
assignee: []
created_date: '2026-10-12 04:49'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: medium
ordinal: 67000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R32, R39, R40, R42, R43, R44, R45, R46.

The development CORS check rejects invalid origins instead of failing. Postgres binds to the loopback; RustFS and Keycloak bind to an address from an environment variable (loopback by default, all interfaces inside the devcontainer). postCreateCommand warns when pnpm is missing. Tests assert what their names claim and cover the missing cases. C# doc comments carry no inline tags. The notes of TASK-3.13 and TASK-3.15 describe linking through the oidc identity only. Wrong comments and README statements are corrected. PROJECT-STATUS.md reflects the merged state.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a request with Origin null in Development, When the API handles it, Then it answers without a server error
- [ ] #2 Given the started local stack outside the devcontainer, When the operator checks the listening sockets, Then Postgres, RustFS and Keycloak listen on 127.0.0.1 only
- [ ] #3 Given the source tree, When the operator searches the C# doc comments for inline tags, Then there are none
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
