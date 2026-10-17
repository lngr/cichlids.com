---
id: TASK-3.43
title: Let members change their public handle
status: To Do
assignee: []
created_date: '2026-09-28 18:52'
updated_date: '2026-10-17 09:30'
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
