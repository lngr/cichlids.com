---
id: TASK-8.1
title: Activity of a member
status: To Do
assignee: []
created_date: '2026-10-17 09:50'
labels: []
dependencies: []
parent_task_id: TASK-8
priority: high
ordinal: 119000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The API knows when a member last used the app, so a return after four weeks or longer can be recognised.

### Scope
- profile.last_seen_at, written by authenticated requests of registered members at most once per hour per member (a set-based update that checks the stored time, no extra read).
- When a request finds a gap of 28 days or more since last_seen_at, the API records the start of the absence (profile.away_since, the previous last_seen_at) before it updates last_seen_at, in the same statement. TASK-8.4 reads and clears it.
- Members without last_seen_at (never seen by the API) count as not away; the legacy last_login_at stays as it is.
- last_seen_at and away_since are private: no DTO shows them to others; the golden snapshot nulls them.
- Provisional profiles are not tracked (their device credential has its own time).

### Reference
- TASK-8, TASK-8.4.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member seen within the last hour, When the member makes another request, Then last_seen_at stays unchanged and no write happens
- [ ] #2 Given a member last seen 30 days ago, When the member makes a request, Then away_since holds the previous last_seen_at and last_seen_at the time of the request
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
