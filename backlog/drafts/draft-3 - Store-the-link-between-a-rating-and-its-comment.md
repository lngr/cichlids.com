---
id: DRAFT-3
title: Store the link between a rating and its comment
status: Draft
assignee: []
created_date: '2026-09-28 15:42'
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

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
