---
id: TASK-7.6
title: Database guard keeps provisional content private
status: To Do
assignee: []
created_date: '2026-10-17 09:30'
labels: []
dependencies: []
parent_task_id: TASK-7
priority: medium
ordinal: 116000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The database itself refuses content of a provisional profile that would reach other people, so a missed check in a write path cannot publish it (ADR-0030: everything a provisional account creates stays private).

### Scope
- Triggers refuse a tank of a provisional profile with a profile or stream visibility other than private, on insert and on update.
- Triggers refuse rows by a provisional profile in the tables of the gated actions of TASK-7.2 (follows, follow requests, comments, ratings, kudos, stories and other rows that reach other people), found from the registration_required checks of the API.
- A registration that turns the provisional profile into a member and the move into an existing profile keep working: the guard reads the kind of the profile at the time of the write.
- The golden snapshot tooling knows the new functions (routines allow-list with definition hashes).

### Reference
- ADR-0030, TASK-7.2, TASK-7.3, TASK-5.22.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a provisional profile, When a write sets one of its tanks to a visibility other than private past the API, Then the database refuses it
- [ ] #2 Given a provisional profile, When a row of a gated action is written for it past the API, Then the database refuses it, and registration and move keep working
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
