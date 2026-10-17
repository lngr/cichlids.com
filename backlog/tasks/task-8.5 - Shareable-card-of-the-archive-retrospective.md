---
id: TASK-8.5
title: Shareable card of the archive retrospective
status: To Do
assignee: []
created_date: '2026-10-17 09:50'
labels: []
dependencies:
  - TASK-8.2
parent_task_id: TASK-8
priority: low
ordinal: 123000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A portrait card summarises the archive retrospective and can be shared from the app.

### Scope
- Portrait card (9:16) with member since, photo count, comments received, the most viewed photo and the app name; no handle unless the member chooses to show it; no data of other members.
- The app renders the card from the retrospective data and hands it to the share sheet of the operating system (native) or offers a download (web).
- The card uses only data the member's own retrospective shows.

### Reference
- MVP-VISION.md 6.1, TASK-8.2.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an archive retrospective, When the member taps share on its last card, Then the app produces a portrait image of the summary and opens the share options
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
