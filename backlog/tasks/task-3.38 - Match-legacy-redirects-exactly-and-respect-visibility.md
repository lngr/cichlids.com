---
id: TASK-3.38
title: Match legacy redirects exactly and respect visibility
status: In Review
assignee:
  - '@claude'
created_date: '2026-10-12 04:48'
updated_date: '2026-10-13 07:22'
labels: []
dependencies: []
references:
  - >-
    app/api/tests/Cichlids.Api.Tests/Features/LegacyRedirects/LegacyRedirectsEndpointsTests.cs
  - app/api/tests/Cichlids.Api.Tests/Features/Species/SpeciesEndpointsTests.cs
  - >-
    app/api/tests/Cichlids.Api.Tests/Features/Community/CommunityEndpointsTests.cs
  - app/mobile/e2e/playwright/legacy-redirects.write.mjs
parent_task_id: TASK-3
priority: medium
ordinal: 62000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R29, R30.

Legacy redirect lookups and searches treat % and _ as plain characters and compare aliases exactly, ignoring case. Redirects lead only to visible content; archived, deleted or unpublished pictures and tanks answer 410 Gone.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the URL /pictures/pic/%25.html, When it is requested, Then the API answers 410
- [ ] #2 Given the legacy URL of an archived picture, When it is requested, Then the API answers 410
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
- Red first: API integration tests (Api.Tests, Testcontainers) for both acceptance criteria and
  for wildcard characters in the species and community searches, then the fix.

### Exact matching
- `LegacyRedirectsQueryService`: compare the legacy alias exactly, ignoring case, without
  `ILIKE` patterns (for example `lower(alias) = lower(@value)` or an escaped pattern), so `%` and
  `_` are plain characters. An unknown alias answers 410 as before.
- `SpeciesQueryService` and `CommunityQueryService`: escape `%`, `_` and the escape character
  in user search input before building the `ILIKE` pattern.

### Visibility
- A redirect resolves only to visible content, using the same visibility rules as the read
  endpoints of pictures and tanks; an archived, deleted or unpublished picture or tank answers
  410 Gone.

### Tests and references
- Api.Tests: `/pictures/pic/%25.html` and an alias with `_` answer 410; the legacy URL of an
  archived picture and of a hidden tank answer 410; a visible picture still redirects; searches
  for `%` and `_` match only literal characters.
- An end-to-end check of the user path on the seeded or write stack, tagged TASK-3.38, that
  requests a wildcard legacy URL and a valid one through the web app or the API.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- `Features/Common/LikePattern.cs` escapes `%`, `_` and the backslash before any `ILIKE`; used by
  the picture alias fallback, the species alias redirect, the species search and the community
  thread search.
- A picture alias redirects only to a published, not deleted post; a tank legacy id only to a
  published, not deleted tank; otherwise 410. Nothing is stored, so content that becomes visible
  again redirects again.
- The seeded `DraftTank` of the API tests has the legacy id 5002 for the draft case.

### Verified by the agent
- Red: 19 of 68 tests in the three affected test classes failed before the fix.
- `dotnet test` green in all three projects (Infrastructure 284, Api 324, Etl 331).
- `pnpm run e2e:write`: 7 of 7 write specs passed, including `legacy-redirects.write.mjs`, which
  calls the API of the write stack directly and changes states only in `cichlids_e2e`.

### Operator checks
- With the dev stack and API running: `curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5045/pictures/pic/%25.html`
  answers 410 (AC #1).
- `pnpm run e2e:write` in `app/mobile` passes `legacy-redirects.write.mjs` (AC #2: archived
  picture answers 410, visible picture redirects).
<!-- SECTION:NOTES:END -->
