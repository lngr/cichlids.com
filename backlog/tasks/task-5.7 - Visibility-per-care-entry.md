---
id: TASK-5.7
title: Visibility per care entry
status: To Do
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 03:48'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 87000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Each care entry has a visibility; private entries never leave the owner, public entries appear in the tank timeline for others.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a private and a public entry of a tank, When another member opens the tank, Then they see only the public entry
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
### Open: default visibility (project owner, 2026-10-16)
- The default must fit the feed idea of the product. Proposals so far (private by default, per-tank setting, public without exact times) do not fit it in the view of the project owner; the decision is deferred.
- Considerations to weigh: exact timestamps reveal absence patterns; data protection by default (GDPR Art. 25(2)); timelines that live from care entries.
- Until the decision only the tank owner reads care entries (as implemented in TASK-5.1). Do not implement this story before the decision.
<!-- SECTION:NOTES:END -->
