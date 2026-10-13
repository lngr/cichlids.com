---
id: TASK-3.42
title: Apply the code and documentation corrections from the MVP review
status: In Progress
assignee: []
created_date: '2026-10-12 04:49'
updated_date: '2026-10-13 11:54'
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

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
- Test-first where behaviour changes (R32, R42); the documentation items are plain corrections.
- Split by file area, so the work does not collide with the TASK-3.41 security changes in the
  auth code: first everything outside `Features/Auth`, `Program.cs` and the mobile auth folder,
  then R32 and the remaining auth comments.

### Items
- R32: the Development CORS check rejects an invalid origin such as `null` instead of throwing;
  Api test.
- R39: Postgres binds to 127.0.0.1; RustFS and Keycloak bind to an address from an environment
  variable (default 127.0.0.1); the devcontainer sets it to 0.0.0.0.
- R40: `postCreateCommand.sh` warns clearly when pnpm is missing.
- R42: the outbox concurrency test uses many events and parallel dispatchers; tests for poison
  message, non-HTTP URL, backoff after a stall, double rating where not covered; the client-core
  test asserts the ApiError type; `HasPendingModelChanges` in the schema tests; unique constraints
  of slugs; direct ImageProcessor tests. Maestro findings belong to TASK-2.25.
- R43: no inline tags in C# doc comments under `app/api`.
- R44: the notes of TASK-3.13 and TASK-3.15 describe linking through the oidc identity only.
- R45: the listed comments and the README screenshot statement match the behaviour; `OUT_DIR` of
  `smoke.mjs` points into an ignored directory inside the repository.
- R46: `PROJECT-STATUS.md` reflects the merged state.
<!-- SECTION:PLAN:END -->
