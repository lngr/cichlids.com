---
id: TASK-3.39
title: Apply the ETL rule corrections from the MVP review
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
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
