---
id: TASK-7.6
title: Database guard keeps provisional content private
status: In Review
assignee: []
created_date: '2026-10-17 09:30'
updated_date: '2026-10-17 09:44'
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

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Gated actions and the tables they write
Derived from the routes declared with RequireRegistered, the registration_required checks of CurrentProfileService.ResolveAsync and TanksWriteService (create and visibility), and the tables of the data model that hold a profile or tank reference:
- Tank visibility: tank.profile_visibility and tank.stream_visibility (TanksWriteService create and update).
- Follows and follow requests: follow (follower_profile_id, target_profile_id, target_tank_id).
- Selected members: tank_selected_member (member_profile_id, tank_id).
- Comments: comment (author_profile_id, tank_id).
- Ratings: rating (profile_id, tank_id).
- Kudos: comment_vote (profile_id), the only vote table of the model.
- Stories: story (author_profile_id) and story_subject (tank_id). Story items (media_link with story_id), the cover, quick share, collection_entry and slug_alias hang on a story or collection of the same profile and cannot exist without a refused parent row.
- Profile media (avatar and header): media_link (profile_id).
- Forum posts and collections: discussion_post (author_profile_id), collection (profile_id); no route writes them for a provisional caller, the move and the removal touch them.
- Left unguarded because they stay private: care data, plans, to-dos, vacations, freezes, uploads, used checksums, web push, snoozes, sessions, device credentials, outbox events.

### Mechanics
- One migration ProvisionalContentGuard with PL/pgSQL functions and BEFORE row triggers; every refusal uses SQLSTATE PV001 and a message that names the table and the rule.
- profile_is_provisional(bigint): STABLE lookup of the profile kind at the time of the write.
- tank_of_provisional_profile_is_private(): BEFORE INSERT OR UPDATE on tank, runs when a visibility is not private; covers insert, visibility updates and a changed profile_id.
- reject_provisional_profile_rows(): one function with the profile columns as trigger arguments, attached to follow, comment, rating, comment_vote, tank_selected_member, story, discussion_post, collection and media_link.
- reject_rows_of_provisional_tanks(): one function with the tank column as trigger argument, attached to follow, comment, rating, tank_selected_member and story_subject.
- Deletes are never guarded, so the removal of a provisional profile and the cleanup service work. Registration turns the kind into member before any content changes; the move updates tank.profile_id to the member profile, which is not provisional.
- Down drops every trigger and function.

### Work
- Red first: Infrastructure tests write past the API (DbContext and raw SQL) and expect PV001 for each guarded table, for tank insert, visibility update and profile change; tests that a member profile, a private tank, an upgrade to member and deletes pass.
- Migration, then the Api tests for registration, move and cleanup run against the guard.
- Golden snapshot: routines.json gets the functions and triggers with their definition hashes, fixture-schema.sql pins the migration, the snapshot unit tests and the migrated schema test run.
- API write paths that can reach the guard are fixed in the API check.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation
- Migration ProvisionalContentGuard: profile_is_provisional(bigint) reads the profile kind at the time of the write; a BEFORE trigger on tank refuses a profile or stream visibility other than private for a provisional profile on insert and update (also when profile_id changes); one profile-column function (reject_provisional_profile_rows) guards follow (follower, target profile), comment, rating, comment_vote, tank_selected_member, story, discussion_post, collection and media_link (profile media); one tank-column function (reject_rows_of_provisional_tanks) guards follow, comment, rating, tank_selected_member and story_subject rows that reference a tank of a provisional profile. Every refusal raises SQLSTATE PV001. Deletes are never guarded. Down drops all triggers and functions.
- No API change: the full API suite shows no write path (registration upgrade, move, cleanup, tank create and visibility) that reaches the guard; the API checks stay the first line and the database error has no special mapping.
- Golden snapshot: routines.json lists the four functions and the guard triggers with their hashes (computed on PostgreSQL 18.6, equal for the migrated database and fixture-schema.sql), fixture-schema.sql pins the migration, fixture.sql turns its provisional profile provisional after its content exists, README updated.
- Tests: ProvisionalContentGuardTests (Infrastructure) writes past the API through the DbContext and raw SQL for every guarded table and for tank insert, visibility update and profile change, and checks that member profiles, private tanks, upgrade to member, a tank move into a member profile, deletes and the migration revert pass. ProvisionalAccountCleanupTests seed their content within the guard.

### Verification (branch)
- .NET Infrastructure 691, API 2392 (1 skipped) of 2393, Etl 350, run one after another; golden-snapshot test_snapshot, test_restore, test_media, test_provisioning and test_migrated_schema green. No app change, no E2E run.
- Not guarded: a profile that changes its kind to provisional while it owns public content; care data and other private tables.
<!-- SECTION:NOTES:END -->
