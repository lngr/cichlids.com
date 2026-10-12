---
id: TASK-3.32
title: Compute every ETL verification expectation from the legacy source only
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: high
ordinal: 54000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R34, with the verify parts of R9, R16 and R19.

The verify step derives all expectations from the legacy database, expects no API-created rows, and names for each comparison the migration rule behind it. It covers the agreed rules: one rating per member and target with self-ratings dropped, empty galleries skipped, vote timestamps converted from America/New_York.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a target where the operator deleted one migrated post, When the verify step runs, Then it reports a post mismatch and the comment comparison is not silently adjusted
- [ ] #2 Given a correct import, When the verify step runs, Then every comparison passes and names its rule in the report
- [ ] #3 Given the imported votes, When the operator queries for a vote older than its comment, Then there is none
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
