---
id: TASK-3.35
title: Create the profile of a first login atomically
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: high
ordinal: 58000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R12, R31 (API part).

Profile and oidc identity of a first login are stored in one transaction; on a conflict with a parallel first call the API resolves the existing profile. A login bound to a non-member profile is refused with a clear response and logged.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a new account, When the app sends several authenticated requests at the same moment right after the first login, Then all succeed and exactly one profile exists
- [ ] #2 Given a login bound to an archived profile, When the app calls the me endpoint, Then the API answers with a clear refusal and no server error
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
