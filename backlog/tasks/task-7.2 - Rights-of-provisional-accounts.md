---
id: TASK-7.2
title: Rights of provisional accounts
status: In Review
assignee: []
created_date: '2026-10-16 20:28'
updated_date: '2026-10-16 21:26'
labels: []
dependencies:
  - TASK-7.1
references:
  - app/mobile/e2e/playwright/provisional-rights.write.mjs
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation (2026-10-16)
- Policy registered (RequireRegistered) answers provisional callers with 403 registration_required before binding; a structural test fails for any authenticated route that is not classified as registered-only or open to provisional callers.
- Open to provisional callers: private tanks (create answers private; any wider level answers registration_required), care, plans, to-dos, photos and videos, guided water changes, decommissioning, time-lapse photos, Me reads, settings, vacations, media library, video uploads, and every anonymous read; a provisional caller is owner of its own tanks and media.
- registration_required: follows and requests, selected members, comments and ratings, story editor, quick share, uploads to stories, publish, avatar and header, onboarding, interests, legacy tanks, region refinement, web push and its snooze.
- Lower limits: 10 uploads per hour, videos up to 30 s, 1 open video upload, 10 videos.
- Follow-up noted: provisional tanks stay private by the API guard; no database check enforces it.
- Verification (branch): .NET 643/2332 (1 skipped)/350, client-core 303, mobile 451, 50 write e2e specs green.
<!-- SECTION:NOTES:END -->
