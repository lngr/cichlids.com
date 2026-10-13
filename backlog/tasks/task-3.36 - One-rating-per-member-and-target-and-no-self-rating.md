---
id: TASK-3.36
title: One rating per member and target and no self-rating
status: In Review
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-13 08:15'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/rating.write.mjs
  - >-
    app/api/tests/Cichlids.Api.Tests/Features/Comments/CommentWriteEndpointsTests.cs
  - >-
    app/api/tests/Cichlids.Infrastructure.Tests/Persistence/ConstraintEnforcementTests.cs
  - >-
    app/api/tests/Cichlids.Infrastructure.Tests/Persistence/SchemaMigrationTests.cs
  - app/mobile/src/components/commentComposerState.test.ts
parent_task_id: TASK-3
priority: medium
ordinal: 60000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R9.

A member has exactly one rating per target; a new rating replaces the previous one, enforced by a unique index. Members cannot rate their own content; the app offers no stars there. The ETL keeps the latest legacy rating per member and target, drops legacy self-ratings (the comment text stays) and reports both counts.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who rated a picture, When the member rates it again, Then the picture has one rating by that member with the new value
- [ ] #2 Given a member viewing their own picture, When the member tries to rate it through the API, Then the API refuses
- [ ] #3 Given the import report, When the operator reads it, Then it lists the collapsed legacy ratings and the dropped self-ratings
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
- Red first at each layer: API integration tests (Api.Tests, Testcontainers) for replace and
  self-rating refusal, a migration test for the unique indexes, client and component tests for
  the hidden stars, then a Playwright write spec for the user path.

### Database
- Two partial unique indexes on `rating`: `(profile_id, post_id)` where both are not null and
  `(profile_id, tank_id)` where both are not null. Anonymous legacy ratings (null profile) stay
  unconstrained. EF configuration plus migration.

### API
- `CommentsWriteService.CreateAsync` writes the rating as an atomic upsert
  (`INSERT ... ON CONFLICT ... DO UPDATE` on the matching partial index): a repeat rating
  replaces stars and timestamp of the existing row, so two parallel requests never create two rows.
- The post aggregate is recomputed after the upsert as before.
- Outbox: `rating.created` for a new row, `rating.updated` (with the previous stars) for a
  replacement.
- Self-rating: when the target post or tank belongs to the acting profile and the request carries
  stars, the endpoint refuses with 403 problem details and writes nothing; a comment without
  stars on own content stays allowed. The OpenAPI document lists the response; the client in
  `client-core` is regenerated.

### App
- The comment composer offers no stars on own pictures and tanks (ownership from the detail
  response and the signed-in profile).

### Tests and references
- Api.Tests: repeat rating gives one row with the new value and a recomputed aggregate; parallel
  repeat ratings give one row; own picture and own tank with stars give 403 and no row; own
  content without stars gives a comment.
- Playwright write spec on the write stack, tagged TASK-3.36: rate a picture twice, one rating
  with the new value; no stars on an own picture.
- AC #3 is delivered by TASK-3.32 (import report); verify the report lines and reference them.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The ETL part of R9 (newest legacy rating per member and target, self-rating drop, counters) and the import report of AC #3 are delivered by TASK-3.32. Anonymous ratings (fe_user = 0) migrate unchanged, so the unique index must allow several rows with a NULL profile_id.

### Implemented
- Database: partial unique indexes `ux_rating_profile_post` and `ux_rating_profile_tank` (both
  columns not null); anonymous ratings stay unconstrained. The migration
  `UniqueRatingPerMemberAndTarget` first removes older duplicates per member and target (keeps
  the newest) and recomputes the aggregates of the affected posts.
- API: one atomic upsert per rating; a replacement takes stars and request time, so the stars show
  at the newest comment of the member, and it clears `legacy_comment_id` so a migrated comment
  shows no stale stars. Outbox: `rating.created` for a new row, `rating.updated` with
  `previousStars` for a replacement. Stars on own content: 403 problem details with
  `code: "self_rating"`, nothing written; a comment without stars on own content stays allowed.
- App: the comment composer offers no stars on own pictures and tanks (owner id from the detail
  response, signed-in profile from `me`); while the profile is unknown the API decides.
- `comment.write.mjs` seeds its picture as `dev-mod`, because rating it as its owner is a
  self-rating.
- OpenAPI and client unchanged: the comment endpoints already list 403.

### Verified by the agent
- `dotnet test`: Infrastructure 284, Api 324, Etl 331; client-core 11; mobile 79 and typecheck;
  `pnpm run e2e:write` 7 of 7 including `rating.write.mjs`.
- AC #3 comes from TASK-3.32 (references `RatingSelectionTests`, `PictureMigrationStepTests` in
  `app/api/tests/Cichlids.Etl.Tests/`); not re-run here.

### Operator checks
- `pnpm run e2e:write` in `app/mobile` passes `rating.write.mjs` (AC #1 and #2).
- The verification report of `etl verify` after a bootstrap lists the collapsed legacy ratings
  and the dropped self-ratings (AC #3).
<!-- SECTION:NOTES:END -->
