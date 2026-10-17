---
id: TASK-9.1
title: Stocking of a tank
status: In Progress
assignee: []
created_date: '2026-10-17 12:36'
updated_date: '2026-10-17 12:39'
labels: []
dependencies: []
parent_task_id: TASK-9
priority: high
ordinal: 125000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The owner keeps the stocking of a tank: groups (species and count) and named fish (name, optional species, count one), each with the day it moved in and, once it left, the day and the reason (died, given away, moved to another tank). Removed inhabitants stay as history (ADR-0031).

### Scope
- API: list (current and history), add, edit, remove with reason; only the owner writes; species from the catalog; a name makes the count one; the moved-in day defaults to today in the member's local date and may lie in the past, never in the future; the removal day is not before the moved-in day.
- Migrated inhabitants have no moved-in day; they count as present since the tank was created.
- The tank page lists the current stocking with named fish first and offers editing to the owner; history is a collapsible section.
- InhabitantDto carries the inhabitant id so stories and entries can refer to it.
- Provisional profiles may keep a stocking (it is private content of their tank).
- Visibility of the stocking follows the profile visibility of the tank.

### Reference
- ADR-0031, ADR-0029, TASK-3.4.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an owner, When the owner adds a group of 12 of a species and a named fish called Bruno, Then the tank page lists Bruno and the group with their moved-in days
- [ ] #2 Given a named fish, When the owner removes it with the reason died, Then it leaves the current stocking and appears in the history with the day and reason
- [ ] #3 Given a member who is not the owner, When the member writes to the stocking, Then the API refuses it
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
### Data model
- Table inhabitant gains name (nullable, up to 80 characters), moved_in_on (date, null for migrated rows), removed_on (date), removal_reason (died, given_away, moved) and owner_profile_id (not null, backfilled from the tank).
- Check constraints: a name implies count 1; removal_reason is set exactly when removed_on is set; removed_on is not before moved_in_on; the name is not blank.
- owner_profile_id is stored on the row. A named fish keeps its identity across the tanks of its member, so the owner cannot be derived from the current tank: a later move or the deletion of the tank would change the answer. The service keeps it equal to the owner of the tank on every write; the content move of a provisional profile updates it and the cleanup of provisional accounts deletes the rows by this column.
- The foreign key to the tank restricts instead of cascading. Tanks are soft deleted (DeletedAt), so no request removes a tank row; the only hard delete is the cleanup of provisional accounts, which deletes the inhabitants of the profile first. A named fish page survives the deletion of its tank.
- The provisional content guard needs no new trigger: the stocking is private content of the private tank of the provisional profile and reaches nobody.
- Migrated rows have no moved-in day; they count as present since the tank was created.

### API
- GET /api/tanks/{tankId}/inhabitants (current and history, follows the profile visibility of the tank, otherwise the 404 of a hidden tank), POST, PATCH /{id}, POST /{id}/removal with a reason and an optional day.
- Writes need the owner; every other caller gets the same 404 as for a hidden tank. Provisional owners write too.
- The local date comes from the client as date and utcOffsetMinutes like the care and streak routes; the moved-in day defaults to it and is never after it; the removal day defaults to it, is never after it and not before the moved-in day (a migrated row compares against the day the tank was created).
- Species are validated against the catalog; a name forces count 1; a group needs a species; a named fish may have none.
- Each write commits with an outbox event carrying ids only: inhabitant.added, inhabitant.updated, inhabitant.removed.
- TankDetailDto.inhabitants lists the current stocking with InhabitantDto(id, species, count, name, movedInOn, removedOn, removalReason), named fish first.

### Clients
- Regenerate the OpenAPI client; client-core maps and validates the requests.
- The tank page shows the stocking with named fish first, an edit flow for the owner (species search from the catalog, count, name, moved-in day, remove with a reason) and the history as a collapsible section; texts in all 11 catalogs.
- Golden snapshot tooling gets COLUMN_HANDLING rules for the new columns (names are free text) and a pinned migration.

### Tests
- API integration tests per acceptance criterion plus name and count, future dates, removal before move-in, unknown species, provisional owner, visibility levels and migrated rows; client-core and mobile unit tests; one Playwright write spec.
<!-- SECTION:PLAN:END -->
