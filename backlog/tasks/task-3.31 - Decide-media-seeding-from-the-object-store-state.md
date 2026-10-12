---
id: TASK-3.31
title: Decide media seeding from the object store state
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: high
ordinal: 53000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R20.

The media seed checks every original and variant in the object store (existence and a SHA-256 checksum stored as object metadata) and uploads only missing or differing objects; for present objects it records the metadata in the database. A run after a database reset therefore finishes in minutes, and objects lost from the store are restored.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a fully seeded object store and a freshly reset database, When the operator runs the media seed, Then it finishes within minutes and its log shows no original uploads
- [ ] #2 Given an original the operator deleted from the object store, When the media seed runs, Then the object exists again with the checksum of the legacy file
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
