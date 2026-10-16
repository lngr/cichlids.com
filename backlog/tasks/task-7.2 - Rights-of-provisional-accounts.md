---
id: TASK-7.2
title: Rights of provisional accounts
status: To Do
assignee: []
created_date: '2026-10-16 20:28'
labels: []
dependencies:
  - TASK-7.1
parent_task_id: TASK-7
priority: high
ordinal: 112000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A provisional account reads everything a visitor reads and keeps its own tanks, care, photos, videos, reminders, streaks and media library, all private; every action others see answers registration_required (ADR-0030).

### Scope
- Write paths that create or widen what others see refuse provisional callers: tank visibility other than private, profile, follow and follow requests, comments, ratings, kudos, stories and quick share, selected members, messages.
- Lower upload quotas and rate limits for provisional accounts.
- Provisional content never appears in public lists, search, feeds or the snapshot of others.

### Reference
- ADR-0030, TASK-5.7, TASK-5.15, TASK-5.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a provisional account, When it creates a tank and logs a water change with a photo, Then both exist and are private
- [ ] #2 Given a provisional account, When it tries to follow, comment, publish a story or make a tank public, Then the API answers registration_required
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
