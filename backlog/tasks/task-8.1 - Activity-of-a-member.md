---
id: TASK-8.1
title: Activity of a member
status: In Review
assignee: []
created_date: '2026-10-17 09:50'
updated_date: '2026-10-17 10:02'
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

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
- profile.last_seen_at and profile.away_since (migration ProfileActivity).
- MemberActivityService.RecordAsync: one UPDATE joined to the oidc identity, filtered to kind member and to last_seen_at null or at least one hour old; sets away_since to the previous last_seen_at when the gap is 28 days or more.
- MemberActivityMiddleware after authorization: registered callers only, failures logged as warnings and never failing the request.
- GetAwaySinceAsync and ClearAwaySinceAsync for the return retrospective.
- Golden snapshot: both columns nulled, fixture and schema pinned to the new migration.

### Tests
- API integration tests: both acceptance criteria, provisional not tracked, exactly 28 days, concurrent requests, no DTO exposure.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- Columns profile.last_seen_at and profile.away_since (migration ProfileActivity); neither is part of any DTO.
- MemberActivityService (Features/Auth) holds the constants RecordInterval (1 hour) and AwayThreshold (28 days). RecordAsync is one statement: a set-based UPDATE joined to profile_identity (oidc subject), restricted to profiles of kind member and to rows whose last_seen_at is null or at least one hour old, as a data-modifying CTE that also returns whether the subject belongs to a member profile. In the same UPDATE away_since takes the previous last_seen_at when the gap is 28 days or more. The statement needs no prior read.
- MemberActivityMiddleware runs once per request after UseAuthorization for registered callers (bearer token and web session cookie principals both carry the Keycloak sub; provisional and anonymous callers are skipped). An exception is logged as a warning and the request continues; cancellation is not logged.
- MemberActivityThrottle (singleton, size-bound MemoryCache of 50000 subjects, entries expire after RecordInterval) remembers per process which subjects were recorded, so a subject sends at most one statement per process and hour. A subject is remembered only when it belongs to a member profile; the first request of a new member (profile created by that request) is therefore recorded by the next request.
- Concurrent requests of one member serialize on the row lock inside the SQL; the later statement re-evaluates the filter against the fresh last_seen_at and writes nothing, so away_since is written once per absence.
- GetAwaySinceAsync and ClearAwaySinceAsync read and clear away_since for the return retrospective.
- Golden snapshot: both columns are nulled in mask.sql and COLUMN_HANDLING and checked by verify; fixture-schema.sql, schema.json, fixture and README follow the migration.

### Verification
- MemberActivityTests and a web session test (cookie principal is recorded) cover both acceptance criteria, provisional callers, the 28 day boundary, concurrency, the throttle and the absence of exposure; the Api, Infrastructure and Etl test projects and the golden-snapshot tests are green.
<!-- SECTION:NOTES:END -->
