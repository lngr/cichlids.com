---
id: TASK-3.37
title: 'Limit comment length, uploads and drafts'
status: In Review
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-13 15:01'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/limits.write.mjs
  - >-
    app/api/tests/Cichlids.Api.Tests/Features/Comments/CommentWriteEndpointsTests.cs
  - >-
    app/api/tests/Cichlids.Api.Tests/Features/Common/JsonRequestSizeLimitTests.cs
  - app/api/tests/Cichlids.Api.Tests/Features/Uploads/UploadLimitsTests.cs
  - app/mobile/src/components/commentComposerState.test.ts
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: medium
ordinal: 61000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R11, R13.

Comments have at most 10000 characters and deletion reasons at most 500; JSON requests are capped at about 64 KB; the app shows a character counter. A member may upload about 30 photos per hour (configurable) and hold at most 50 open drafts; drafts untouched for 30 days are deleted with their files. Reaching a limit shows a clear message in the app.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a comment of 10001 characters, When the member submits it, Then the API refuses with a clear message
- [ ] #2 Given a member with 50 open drafts, When the member uploads another photo, Then the app shows the draft limit message
- [ ] #3 Given a draft untouched for 31 days, When the cleanup runs, Then the draft and its objects are gone
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
- Red first: Api.Tests (Testcontainers) per limit, mobile unit tests for the counter and the
  messages, then a Playwright write spec for the user path.

### Comments
- New comments at most 10000 characters, deletion reasons at most 500; the API refuses longer
  input with 400 problem details and a code (`comment_too_long`, `reason_too_long`). Legacy
  comments stay unchanged (no database constraint).
- JSON endpoints accept at most about 64 KB per request (request size limit on JSON endpoints,
  multipart upload unaffected); a larger body gets 413.
- The composer shows a character counter and blocks sending above the limit.

### Uploads and drafts
- At most 30 uploads per member and hour (configurable), counted from the stored uploads so the
  limit survives restarts; above it 429 problem details `upload_rate_limited`.
- At most 50 open drafts per member; above it 409 problem details `draft_limit_reached`.
- A hosted cleanup service deletes drafts untouched for 30 days (configurable) with their
  objects, idempotent and safe for several API instances.
- The app translates the new codes in English and German and shows them at the upload form.

### Tests and references
- Api.Tests for each limit and the cleanup; mobile tests for counter and messages; write spec
  tagged TASK-3.37 for the draft limit message (AC #2) and the long comment (AC #1).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- Comments: at most 10000 characters after trimming (400 `comment_too_long`), deletion reasons at
  most 500 (400 `reason_too_long`). JSON requests: a declared body above 64 KB gets 413
  `request_too_large`; bodies without length are capped at 64 KB; multipart uploads keep their own
  limit.
- Uploads: 50 open drafts give 409 `draft_limit_reached` (checked first), the hourly limit 429
  `upload_rate_limited`, counted from `media_item.created_at`. Two parallel uploads at the limit can
  both pass (soft by one or two).
- Cleanup: `DraftCleanupHostedService` with `DraftCleanupService` deletes stale drafts through the
  discard path including objects; the condition sits in the delete statement, so parallel runs are
  safe.
- Configuration (`appsettings.json`, positive values enforced at start): `UploadLimits:MaxUploadsPerHour`
  30, `UploadLimits:MaxOpenDrafts` 50, `DraftCleanup:Enabled` true, `DraftCleanup:Interval`
  01:00:00, `DraftCleanup:Retention` 30.00:00:00, `DraftCleanup:BatchSize` 100.
- App: counter "n / 10000" in the composer, sending disabled above the limit; English and German
  texts for the four codes.

### Verified by the agent
- `dotnet test`: Infrastructure 304, Api 347, Etl 338; client-core 13; mobile 114 and typecheck;
  `pnpm run e2e:write` 10 of 10 including `limits.write.mjs` (AC #1 and #2); AC #3 by the API
  cleanup test.
- Red phase not observed: the tests were written before the code but not run against the old
  code.

### Open before review
- The draft age is `post.created_at`; R13 says untouched for 30 days. Needs a change timestamp on
  drafts (migration) and the cleanup on it.
- `client-core/openapi.json` and the generated client are not regenerated for the new problem
  responses.
- The chunked 413 path is tested with a fake body-size feature only.

### Resolved open points
- Drafts age from `post.updated_at` (migration `AddPostUpdatedAt`, backfilled from `created_at`,
  set on upload and publish); the cleanup keeps a draft created 40 days ago and changed 2 days
  ago and removes one untouched for 31 days (`UploadLimitsTests`). Red observed.
- `client-core/openapi.json` lists the 409 and 429 problem responses of the upload and the 400
  problem response of the comment deletion.
- Still open: the chunked 413 path is tested with a fake body-size feature only.
<!-- SECTION:NOTES:END -->
