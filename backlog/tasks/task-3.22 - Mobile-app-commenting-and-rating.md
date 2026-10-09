---
id: TASK-3.22
title: Mobile app commenting and rating
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-09-28 15:45'
labels: []
dependencies:
  - TASK-3.19
  - TASK-3.14
references:
  - app/mobile/e2e/playwright/comment.write.mjs
  - app/mobile/e2e/maestro/comment-rating.yaml
parent_task_id: TASK-3
priority: medium
ordinal: 42000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Posting comments with rating from the picture and tank detail screens.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a logged in user in the app, When they post a comment with a rating, Then it renders in the detail screen, verified by a Maestro flow
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
A comment composer on the picture detail and the tank detail screens posts to the existing TASK-3.14 endpoints (POST /api/pictures/{slug}/comments, POST /api/tanks/{id}/comments with body and/or stars 1..5) through the client-core helpers createPictureComment/createTankComment, authenticated by the TASK-3.21 AuthProvider.

### UI (app/mobile/src/components/CommentComposer.tsx)
- Multiline text input, a five-star picker (tap to set, tap again to clear), submit button; submit enabled when text or stars are present; 48dp touch targets; theme tokens; i18n keys in en/de.
- Anonymous users see a Log in to comment prompt that calls login().
- On success: the created comment is prepended to the list without a full reload, the rating summary (average/count) of the picture refetches, the input clears; errors show inline with retry.
- CommentItem shows a rating given together with a comment (created rating from the response) as stars.
- Used in app/(tabs)/gallery/[slug].tsx and app/(tabs)/tanks/[id].tsx as list header section.
- testIDs: comment-input, comment-star-N, comment-submit, comment-item.

### Red-Green order
1. Component/unit tests for the composer state logic (submit enabled rules, star toggle) with the mobile test setup introduced in TASK-3.21, red first.
2. Playwright spec app/mobile/e2e/playwright/comment.mjs on the isolated write stack from TASK-3.21: seed one published picture through the API with a dev-user token (password grant on cichlids-app), log in through the Keycloak page, open the picture detail, write a comment with 4 stars, submit, assert the comment text and its stars render in the detail and survive a reload. Tagged TASK-3.22, referenced via --ref.
3. Maestro flow app/mobile/e2e/maestro/comment-rating.yaml tagged TASK-3.22 (unexecuted, Maestro runner blocked).

### Affected files
- app/mobile/src/components/CommentComposer.tsx, CommentItem.tsx, app/(tabs)/gallery/[slug].tsx, app/(tabs)/tanks/[id].tsx, src/i18n/*.json
- app/mobile/e2e/playwright/comment.mjs, e2e/maestro/comment-rating.yaml, package.json scripts

### Risks and assumptions
- The tank comment path shares the component; its E2E coverage is the component test because the isolated write stack has no tanks (fresh schema). A tank seed can be added to the write stack if needed.
- The AC names a Maestro flow; green evidence is the Playwright web flow (see TASK-3.21 risk).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Delivered
- CommentComposer on the picture detail and the tank detail: multiline text, five stars (tap sets, tapping the selected star clears), submit with busy state, inline error with retry, login prompt for anonymous users. The created comment is prepended; the picture detail refetches its rating summary.
- API: the comment list DTO carries stars, the rating given together with the comment (paired through legacy_comment_id for migrated rows, through the shared target, author and creation timestamp for rows the API writes in one request). The comment list shows these stars; it showed the vote score as stars before this change.
- usePagedList: next-page offset from the number of server items received; posted comments stay visible across a first-page load.

### Tests
- Unit: mobile 59 (composer state rules, paged list state), client-core 11; Api.Tests comment tests 21 incl. stars pairing.
- Playwright on the write stack: comment.write.mjs (anonymous prompt, login, empty submit disabled, star toggle, injected 500 with retry, comment with 4 stars, rating summary, reload).

### Follow-ups
- DRAFT-3 stored link rating.comment_id; DRAFT-4 repeated ratings per member and target; DRAFT-5 view counting in the seeded dev database.
- The tank path is covered by the shared component and unit tests only; the write stack has no tanks.

### AC gap
The AC names a Maestro flow. comment-rating.yaml is written for the native Android build and has not been executed (PROJECT-STATUS F13).
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
