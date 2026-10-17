---
id: TASK-9.1
title: Stocking of a tank
status: To Do
assignee: []
created_date: '2026-10-17 12:36'
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
