---
id: TASK-5.22
title: Keep the golden snapshot in step with the schema
status: In Review
assignee: []
created_date: '2026-10-16 15:16'
updated_date: '2026-10-16 16:27'
labels: []
dependencies: []
references:
  - tools/golden-snapshot/test_migrated_schema.py
  - tools/golden-snapshot/test_snapshot.py
parent_task_id: TASK-5
priority: high
ordinal: 108000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The golden snapshot tooling (tools/golden-snapshot: fixture-schema.sql, schema.json, mask.sql, snapshot and restore, CI job) matches the current schema, including the media links of ADR-0029, and masks every table added since its pinned migration. Without it the snapshot of staging and preview data describes a schema the application no longer has.

### Masking of the newer tables (interim decisions, to be reviewed)
- Care entries, notes, to-dos, measurements, plans and plan history: notes and to-do texts get generated text of the same length; values, kinds, times and plan rules are kept.
- Water change sessions with steps and evidence state: kept; evidence photos follow the media rules of the snapshot (only public variants of selected public posts).
- Web push subscriptions and sent reminders, web sessions: empty.
- Vacations, streak freezes, visibility consents, reminder snoozes: kept (no free text), consent timestamps kept.
- Video uploads and jobs: empty; videos stay excluded.
- Profile settings (streak switch): kept.

### Reference
- ADR-0018, ADR-0029, TASK-5.20.1.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the current migrations, When the golden snapshot schema check runs, Then fixture-schema.sql and schema.json match the migrated schema
- [ ] #2 Given a database with care notes, to-dos and web push subscriptions, When a snapshot is created, Then the note and to-do texts are generated text of the same length and no subscription or session remains
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation (2026-10-16)
- fixture-schema.sql regenerated from the EF model and pinned to 20261016155023_MediaLinks; schema.json from the migrated database (42 tables); a CI step migrates a synthetic database and compares schema, fixture tables and pinned migration.
- TEXT_HANDLING in snapshot.py lists every text, array and JSON column with its rule; a test fails for a text column without a rule.
- Masking beyond the story: media_link.caption as generated text; decommissionings whose proof photos fall outside the media subset are removed (required restrict keys); avatar and header links are removed; other links of retained photos stay with masked captions; photo selection takes the first item link by position.
- Fixed: the migration history columns are migration_id and product_version (snake case); fixture.sql sets tank.care_reminders_enabled explicitly because the EF model lacks the default true of the migration.
- Verification: golden-snapshot python tests green in its image against a throwaway postgres; actionlint clean; the CI step itself not run on GitHub.

- Privacy review fixes: private scopes removed (vacations, snoozes, to-dos, comment votes, only-me entries with their sessions, care, sessions, freezes, plans, decommissionings and consents of tanks with a non-public stream, inhabitants and tank media links of tanks with a non-public profile, entries of non-public collections, comments and ratings of tanks with a non-public profile); media capture metadata nulled; COLUMN_HANDLING covers every column and a test fails for a column without a rule; README describes the result as pseudonymized (stable ids). Open for the project owner: renumbering ids.
<!-- SECTION:NOTES:END -->
