---
id: TASK-5.22
title: Keep the golden snapshot in step with the schema
status: To Do
assignee: []
created_date: '2026-10-16 15:16'
labels: []
dependencies: []
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
