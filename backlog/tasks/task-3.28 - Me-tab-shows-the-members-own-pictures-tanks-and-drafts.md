---
id: TASK-3.28
title: 'Me tab shows the member''s own pictures, tanks and drafts'
status: In Review
assignee: []
created_date: '2026-09-29 10:10'
updated_date: '2026-10-14 11:01'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/me-content.write.mjs
  - app/mobile/src/profile/useProfileLists.test.ts
parent_task_id: TASK-3
priority: medium
ordinal: 48000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A logged-in member sees their own content in the Me tab without leaving it: profile header, their published pictures, their tanks and their drafts.

### Context
- The public profile screen (app/profile/[id]) shows header, pictures and tanks of any member with tabs; the Me tab shows name, handle, avatar, a link to that public profile, the drafts list and logout.
- The API already serves the data: GET /api/profiles/{id}/pictures, /api/profiles/{id}/tanks, /api/me/drafts.

### Scope
- Me tab: profile header, tabs Pictures, Tanks and Drafts (reusing the public profile building blocks), logout.
- The link to the public profile stays.
- i18n en/de, theme tokens, 48dp targets.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a logged-in migrated member with published pictures, tanks and drafts, When they open the Me tab, Then they see their pictures, can switch to their tanks and to their drafts, and each list matches their public profile respectively their drafts in the API
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
- Red first: component and hook tests for the Me tab sections and a Playwright write spec tagged
  TASK-3.28 that signs in a member with pictures, tanks and drafts on the write stack and
  compares the lists with the API.
- Reuse the building blocks of the public profile screen (header, tabs, picture and tank lists);
  Me tab: header, tabs Pictures, Tanks, Drafts, link to the public profile, logout.
- i18n en/de, theme tokens, 48dp targets, error and empty states like TASK-3.40.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- Me tab: profile header, tabs Pictures (default), Tanks and Drafts with pagination and retry on
  errors, link to the public profile, logout; shared profile building blocks
  (`ProfileContentList`, `ProfileTabs`, `useProfileLists`) with accessible tabs; i18n en/de.

### Verified by the agent (Codex)
- Red: missing modules, then the e2e spec timed out on the missing `me-tab-pictures`. Green:
  mobile 184 tests and typecheck; `pnpm run e2e:write` 12 of 12 including `me-content.write.mjs`.
- Not verified: native Android and iOS.

### Operator checks
- `pnpm run e2e:write` in `app/mobile` passes `me-content.write.mjs` (AC #1).
<!-- SECTION:NOTES:END -->
