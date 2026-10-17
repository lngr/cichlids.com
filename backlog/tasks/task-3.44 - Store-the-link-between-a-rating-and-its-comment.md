---
id: TASK-3.44
title: Store the link between a rating and its comment
status: In Review
assignee: []
created_date: '2026-09-28 15:42'
updated_date: '2026-10-17 09:41'
labels: []
dependencies: []
parent_task_id: TASK-3
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A rating written together with a comment carries a stored reference to that comment, so the comment list reads the pairing directly.

### Context
- The comment list pairs a rating with its comment through legacy_comment_id (migrated rows) or through the shared target, author and creation timestamp (rows written by the API in one request).
- Any write path that stamps the two rows separately (rating edits, retries, split transactions) breaks that pairing silently.

### Scope
- Nullable unique rating.comment_id with a foreign key to comment, set by the comment write service.
- ETL backfill by joining rating.legacy_comment_id to comment.legacy_id; verify step checks the count.
- Comment list query reads the stars through comment_id only.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a rating written together with a comment, When the comment list loads, Then the stars come from the stored comment reference of the rating
- [ ] #2 Given migrated ratings with legacy comment ids, When the ETL runs, Then every pair gets its comment reference and the verify step checks the count
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
- Add nullable unique rating.comment_id with a foreign key to comment, ON DELETE SET NULL. Comments are hidden by a soft delete (deleted_at) and keep the link; a hard delete of a comment (removal of provisional content, snapshot masking) leaves the rating and its stars in the aggregates without a comment.
- Targets: comment and rating exist for stories and tanks only (exactly one of story_id, tank_id), so one reference covers both.

### Steps
- Red tests first: API list test reads stars through comment_id; write path stores the reference (new comment, repeat rating, rating without comment clears it); ETL test expects comment_id for migrated pairs; verify step test expects the count check.
- Domain and configuration: Rating.CommentId, unique index, foreign key.
- Migration RatingCommentReference: add column, index and foreign key, backfill migrated rows by legacy_comment_id to comment.legacy_id and API rows by target, author and creation timestamp.
- CommentsWriteService sets comment_id in the rating upsert in the same transaction.
- CommentsQueryService reads stars by comment_id only; the legacy id and timestamp pairing is removed.
- ETL CommentMigrationStep sets comment_id by joining legacy_comment_id to legacy_id; VerifyStep counts the pairs.
- Golden snapshot: COLUMN_HANDLING rule for rating.comment_id, fixture schema pins the new migration, tests adjusted.
- Regenerate the client if the contract changes; run API, ETL, Infrastructure and snapshot tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- rating.comment_id: nullable, unique index, foreign key to comment with ON DELETE SET NULL (migration RatingCommentReference). A moderation delete is soft and keeps the link; a hard delete (removal of provisional content, snapshot masking) leaves the rating and its stars without a comment.
- Targets: comment and rating exist for stories and tanks only, one reference covers both.
- Migration backfill: migrated rows by legacy_comment_id to comment.legacy_id, API rows by member, target and creation moment (one comment per rating).
- CommentsWriteService sets comment_id in the rating upsert in the same transaction; a repeat rating takes the comment of the new request (null for stars only), legacy_comment_id is cleared as before.
- CommentsQueryService reads the stars through comment_id only; the legacy id and timestamp pairing is removed.
- ETL: CommentMigrationStep links ratings to comments by legacy ids at the end of the step (idempotent); VerifyStep row rating_comment_reference compares the expected count of ratings whose legacy row has comment text with the joined count.
- Golden snapshot: comment_id kept in COLUMN_HANDLING, schema.json and fixture-schema.sql pin the new migration, fixture and test cover the reference.

### Tests
- API 2393 passed (1 skipped), ETL 351 passed, Infrastructure 646 passed, golden snapshot 20 tests plus migrated-schema checks passed.
- The API contract is unchanged, so the client is not regenerated and no E2E spec is affected.
<!-- SECTION:NOTES:END -->
