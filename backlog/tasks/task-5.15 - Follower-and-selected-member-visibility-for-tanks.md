---
id: TASK-5.15
title: Follower and selected-member visibility for tanks
status: In Review
assignee: []
created_date: '2026-10-16 06:55'
updated_date: '2026-10-16 17:55'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/follow-visibility.write.mjs
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation (2026-10-16)
- Levels public > followers > selected > private for profile and history; a level reaches its own members and every narrower level; selected members are named by the owner and need not follow; history never wider than the profile.
- Follow: public tanks immediately, followers tanks by request (approve, decline, withdraw, remove); selected-level and private tanks cannot be followed; follows from the public time stay approved when the owner narrows to followers; open requests are approved when a tank becomes public; declined, removed or withdrawn rows are deleted.
- Selected members by handle, up to 100 per tank; suspended profiles answer like unknown handles and count as anonymous viewers.
- Routes under /api/tanks/{id}: follow (GET, PUT, DELETE), followers, follow-requests (approve, decline), selected-members; outbox events with ids only; triggers bump the media visibility generation on every audience change.
- Security review fixes: hidden tanks answer like missing ones on every follow path, the follow teaser carries the owner only, suspended members cannot follow and lose access.
- App: four levels in the settings (consent stays an explicit public yes or no), follow button states, owner screens; 10 catalogs translated by a subagent and checked for key and placeholder parity only.
- Verification (branch): .NET 629/2015 (1 skipped)/349, client-core 257, mobile 437, 44 write e2e specs green.
<!-- SECTION:NOTES:END -->
