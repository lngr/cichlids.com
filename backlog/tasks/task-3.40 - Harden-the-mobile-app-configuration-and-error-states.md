---
id: TASK-3.40
title: Harden the mobile app configuration and error states
status: In Progress
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-13 08:22'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: medium
ordinal: 64000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R25, R26, R27, R41.

Builds outside development fail when the API or Keycloak address is missing; app.json carries no localhost values; eas.json defines development, staging and production profiles. The login button stays usable and reloads the discovery document on tap, showing a clear message on failure. Paged lists recover from overtaken load-more requests. Comment load errors show an error with retry, the busy button has an accessibility label, and API error codes are translated.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a staging build without EXPO_PUBLIC_API_URL, When the operator builds the app, Then the build fails and names the variable
- [ ] #2 Given Keycloak unreachable at app start, When the member taps login, Then the app shows a clear message with a retry
- [ ] #3 Given the gallery while more items load, When the member changes the sort, Then scrolling to the end loads further pages
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
- Red first: unit tests (vitest) for config validation, the paged list race, the login retry and
  the error translation; then the change.

### Configuration (R25)
- `localhost` defaults only in development. Outside development the app config (`app.config.ts`
  or a prebuild check) fails the build and names a missing `EXPO_PUBLIC_API_URL` or
  `EXPO_PUBLIC_KEYCLOAK_URL`. `app.json` holds no localhost values. `eas.json` defines the profiles
  development, staging and production with their addresses (placeholders where the domain is not
  decided yet, clearly marked).

### Login (R26)
- The login button stays usable; a missing discovery document is loaded on tap; a failure shows a
  clear message with retry and logs the error.

### Paged lists (R27)
- A new load resets the load-more state in `usePagedList`; a test reproduces load-more overtaken
  by a filter change.

### Errors and accessibility (R41)
- Comment load errors show an error with retry instead of "no comments"; the busy button has an
  accessibility label; API error codes are translated in English and German.

### End-to-end
- Playwright web spec tagged TASK-3.40: gallery sort change during load-more still loads further
  pages; login with unreachable Keycloak shows the message with retry.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The API problem details carry a stable `code` extension field (for example `non_member_profile`) that the generated client type does not declare yet.
<!-- SECTION:NOTES:END -->
