---
id: TASK-4.8
title: Today as the starting point before care content
status: To Do
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 09:01'
labels: []
dependencies: []
parent_task_id: TASK-4
priority: medium
ordinal: 79000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Today is the first tab; before the care features of slice 2 it shows a minimal, useful start.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a signed-in member, When they open the app, Then Today shows its start content and links into their tanks and Discover
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Content of Today (decided by the project owner, 2026-10-15)
1. Greeting with the name; returning legacy members see "Welcome back, a member since <year>".
2. Your tanks as cards with the newest photo, linking to the tank page; without a tank "Create
   your first tank".
3. Open drafts if any: "Continue editing".
4. "On this day X years ago" for legacy members: an own photo from this calendar day of an
   earlier year, if one exists.
5. Tank of the week from the hall of fame of the member's interest region, linking to Discover.
Sections 2 to 4 disappear without own content, and the tank of the week moves up. Slice 2 adds
due care tasks and the streak above section 2.

- The target Today of the mockup and the vision is kept in DRAFT-11; each of its parts replaces
  the reduced Today as soon as the slice that delivers its data exists.
<!-- SECTION:NOTES:END -->
