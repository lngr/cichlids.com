---
id: TASK-3.28
title: 'Me tab shows the member''s own pictures, tanks and drafts'
status: To Do
assignee: []
created_date: '2026-09-29 10:10'
labels: []
dependencies: []
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
