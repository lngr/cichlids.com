---
id: TASK-5.15
title: Follower and selected-member visibility for tanks
status: To Do
assignee: []
created_date: '2026-10-16 06:55'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 95000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal (concept decided by the project owner, 2026-10-16; see TASK-5.7)
Adds the two middle levels of the tank visibility concept: followers (follow requests that the owner approves; only approved followers see the tank or its stream) and selected members (the owner names members, for example family and friends). Both levels apply to the tank profile and, restricted further, to the care stream, as in TASK-5.7.

### Reference
- TASK-5.7 (levels public and private, enforcement), the Follow model of the archive, DRAFT-15 (messages between mutual followers).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a tank with the level followers, When a member sends a follow request and the owner approves it, Then that member sees the tank and its stream while other members do not
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
