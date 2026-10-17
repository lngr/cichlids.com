---
id: TASK-3.43
title: Let members change their public handle
status: In Review
assignee: []
created_date: '2026-09-28 18:52'
updated_date: '2026-10-17 09:50'
labels: []
dependencies: []
parent_task_id: TASK-3
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A member changes the public handle (profile.username) in the app.

### Context
- The identity model separates the account (Keycloak user), its logins (password with login username or email, Google, Facebook, passkeys) and the public handle (ADR-0023).
- Migrated members without a usable legacy name and new members get a generated handle user<number>.

### Scope
- API endpoint to change the handle with rules: allowed characters, length, uniqueness, never an email address; rate limit.
- Legacy redirects and profile links keep working (profiles resolve by id).
- Me tab: edit the handle.

### Rules
- 3 to 30 characters: lowercase letters a to z, digits, underscore and dot; it starts with a letter and ends with a letter or digit, without two dots in a row. Input is lowercased before the check.
- Unique without regard to case among all profiles, and never the form of a generated name (user followed by eight digits, guest_ followed by a number, provisional- prefixes) nor a reserved name (admin, administrator, cichlids, support, help, moderator, system, api, www, root, official).
- One change per 30 days. A released handle stays reserved for its member for 90 days: nobody else takes it, the member may take it back.
- A provisional profile has no public handle; the change needs a registered account (registration_required).
- Profile links and legacy redirects resolve by id and keep working.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a registered member, When the member sets a valid free handle, Then the profile shows the new handle and the old one is reserved for that member for 90 days
- [ ] #2 Given a member who changed the handle less than 30 days ago, When the member changes it again, Then the API refuses with handle_change_too_soon and names the date of the next change
- [ ] #3 Given a handle that is taken, reserved, generated in form or invalid, When a member asks for it, Then the API refuses with a problem code for that reason and the app shows it at the field
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
### Domain rules
- HandleRules (Cichlids.Infrastructure.Identity) lowercases the input and classifies it: invalid, generated form (user plus eight digits, guest_ plus number, provisional- prefix), reserved name (fixed list plus the placeholder names of ReservedUsernames) or valid. Unit tests cover every class.

### Storage
- profile.handle_changed_at (timestamptz, null until the first change) and the table released_handle (profile_id, handle lowercase, released_at, reserved_until; unique handle, cascade on the profile).
- A unique index on lower(username) makes the uniqueness case-insensitive under concurrent requests. Existing profiles that differ only in case are renamed with their id as suffix in the same migration; the ETL compares handles without regard to case.
- Migration via dotnet ef; the golden snapshot tool gets COLUMN_HANDLING rules and empties released_handle.

### API
- PUT /api/me/handle (RequireRegistered, so a provisional caller gets 403 registration_required) with the profile row locked in a transaction.
- Problem codes: handle_invalid (400), handle_generated (400), handle_reserved (400), handle_taken (409, also for a handle reserved for another member), handle_change_too_soon (429 with nextChangeAt and Retry-After).
- A change writes the released handle (reserved 90 days), deletes the reservation of the handle taken back, sets handle_changed_at and records the outbox event profile.handle_changed with ids only.
- GET /api/me names the earliest next change (handleChangeAvailableAt).
- First-login handle choice skips handles reserved for another member.
- Profile links (/members/{id}), the legacy redirects (legacy uid to profile id) and the API routes resolve profiles by id, so no route needs the old handle.

### Clients
- OpenAPI client regenerated; client-core gets me.setHandle with the problem codes; the Me tab gets a handle editor with the error at the field and the date of the next change; all 11 catalogues get the texts.

### Tests
- API integration tests for each acceptance criterion and rule, handle rules unit tests, client-core and mobile unit tests, one Playwright write spec.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- HandleRules (Cichlids.Infrastructure.Identity) lowercases and trims the input and classifies it as valid, invalid, generated form or reserved name. Generated forms and reserved names are reported before malformed input.
- Migration ProfileHandleChange: profile.handle_changed_at, the table released_handle (lowercase handle, unique, reserved_until) and the unique index ux_profile_username_lower on lower(username). The index is created in raw SQL because EF Core cannot model an expression index; the model keeps the case-sensitive unique index. Profiles that differ only in case keep the handle of the oldest profile, the others get their id as suffix in the migration (the local development database has five such pairs). The ETL compares legacy usernames without regard to case.
- PUT /api/me/handle (RequireRegistered, so a provisional caller gets 403 registration_required). The profile row is locked in a transaction; the unique index decides between concurrent requests for the same handle. Codes: handle_invalid, handle_generated, handle_reserved (400), handle_taken (409, also for a handle reserved for another member), handle_change_too_soon (429 with nextChangeAt and Retry-After).
- A change releases the old handle for 90 days, deletes the reservation of a handle taken back, sets handle_changed_at and records profile.handle_changed with the profile id only. Asking for the current handle answers 200 without a change; a change of case only counts as a change without a reservation.
- GET /api/me returns handleChangeAvailableAt. The handle choice of a first login skips handles reserved for another member and handles that differ only in case.
- client-core: me.setHandle, handleProblemOf, ApiError.problem with the extension members of a problem body. Mobile: HandleSettings in the Me tab (not for provisional accounts), error at the field, date of the next change, texts in all 11 catalogues.
- Golden snapshot: handle_changed_at is set to NULL, released_handle is an emptied table, the migration is pinned and the migrated-schema check passes against a database migrated by dotnet ef.

### Findings
- Profile links (/members/{id}, /profile/{id}, /api/profiles/{id}) and the legacy redirects (/members/{legacyUid} to /members/{profileId}) resolve by id; no route resolves a handle, so no old handle needs to lead to a profile. The tests cover the API route and the legacy redirect after a change.
- Existing handles can contain characters the rule does not allow (hyphen, capitals). They stay valid until the member changes them; others cannot ask for such a handle, as it is invalid.
- The rate limit of the scope is the 30 day rule; there is no further request limiter on the route.

### Verification
- dotnet tests: Infrastructure 693, Etl 350, Api 2426 (1 skipped) passed; client-core 343, mobile 536 unit tests passed; golden snapshot unit and database tests passed; write E2E spec handle.write.mjs passed on its own.
<!-- SECTION:NOTES:END -->
