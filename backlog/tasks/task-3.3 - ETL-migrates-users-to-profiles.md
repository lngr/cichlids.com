---
id: TASK-3.3
title: ETL migrates users to profiles
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-08 20:29'
labels: []
dependencies:
  - TASK-3.1
parent_task_id: TASK-3
priority: high
ordinal: 23000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
fe_users to profiles with the agreed hygiene filter, keeping auth0 sub mappings, openid identities and email join data for the Keycloak import.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the restored legacy MySQL, When the profile ETL step runs twice, Then every non-filtered fe_user exists exactly once as a profile with its legacy_id and its identity mappings are preserved for the Keycloak import
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Real double run: 6446 profiles (content filter), 7253 identities (auth0 377, email 6440, legacy-openid 436), 4 username collisions suffixed, no duplicates after second run. Auth0 subjects of contentless accounts intentionally not migrated (journal F7).
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
