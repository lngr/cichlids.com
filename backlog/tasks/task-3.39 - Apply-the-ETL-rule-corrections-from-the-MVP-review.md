---
id: TASK-3.39
title: Apply the ETL rule corrections from the MVP review
status: In Progress
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-13 08:22'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: medium
ordinal: 63000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R15, R16, R17, R19, R21, R35, R37.

Placeholder usernames are reserved for new accounts. Empty legacy galleries are skipped by rule and counted. Generated slugs have eight characters. Vote timestamps are read as America/New_York and converted to UTC. The image locator rejects paths outside the image root and matches bare filenames exactly. Anonymous records are summarized in one report line per kind. A check constraint enforces the discussion post author invariant.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a new account choosing the username community-archive, When it signs up, Then the name is refused
- [ ] #2 Given an import, When the operator reads the report, Then it shows the skipped empty galleries and one summary line per anonymous kind
- [ ] #3 Given a newly published picture, When the operator reads its URL, Then the generated slug has eight characters
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
- Red first per rule: ETL and API tests (Etl.Tests, Api.Tests, Infrastructure.Tests with
  Testcontainers) before each change. First check what TASK-3.32 already delivered (vote times in
  America/New_York, skipped empty galleries in the report) and only add what is missing.

### Rules
- R15: the usernames `community-archive`, `former-member-<id>` and `forum-member-<id>` are
  reserved for new accounts (Keycloak registration and the API profile creation refuse them).
- R16: empty legacy galleries are skipped by rule, counted in the report, class comment and
  verify step follow the documented rule (check the TASK-3.32 state).
- R17: generated slugs have eight characters; tests assert 8.
- R19: vote timestamps from America/New_York to UTC with daylight saving days covered (check the
  TASK-3.32 state).
- R21: `LegacyImageLocator` reports paths outside the image root as missing and matches bare
  filenames exactly, without wildcard semantics.
- R35: the archive profile is resolved once per run; anonymous records give one report line per
  kind instead of one warning per row.
- R37: a check constraint enforces the discussion post author invariant (guest post without
  author profile, otherwise without poster name), with migration and test.

### End-to-end
- AC #1 on the write stack (registration or profile creation with `community-archive` is
  refused), tagged TASK-3.39; AC #2 and #3 through the ETL tests and the report of `etl verify`.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
R19 (vote timestamps from America/New_York to UTC) moved to TASK-3.32. The R16 verify row (skipped empty galleries, legacy-derived) is in TASK-3.32; the R16 step counter and class comment stay here. R35 touches the same CommentMigrationStep methods, so this task follows TASK-3.32.
<!-- SECTION:NOTES:END -->
