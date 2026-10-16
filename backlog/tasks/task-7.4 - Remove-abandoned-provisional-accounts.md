---
id: TASK-7.4
title: Remove abandoned provisional accounts
status: In Review
assignee: []
created_date: '2026-10-16 20:28'
updated_date: '2026-10-16 21:18'
labels: []
dependencies:
  - TASK-7.1
references:
  - app/mobile/e2e/playwright/provisional-cleanup.write.mjs
parent_task_id: TASK-7
priority: medium
ordinal: 114000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A provisional profile without a signed request for 12 months is deleted with its content, media files included (decided by the project owner, 2026-10-16).

### Reference
- ADR-0030, TASK-5.20.4 (library deletion of files).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a provisional profile whose last signed request is 12 months and a day ago, When the cleanup runs, Then the profile, its tanks, care data and media files are deleted
- [ ] #2 Given a provisional profile used yesterday, When the cleanup runs, Then it stays
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
- ProvisionalAccountCleanupService with a hosted loop (enabled, 6 h, InactivityMonths 12, batch 50): a provisional profile whose latest signed request over all credentials (or its creation without credentials) is strictly older than now minus 12 months is removed in one transaction per profile under FOR UPDATE SKIP LOCKED on the profile and FOR UPDATE on its credentials, with a re-check; stored objects are deleted after commit; no outbox events; logs carry counts and the profile id only.
- The token exchange refuses when the credential vanished between check and update.
- Verification (branch): .NET 643/2206 (1 skipped)/350, 50 write e2e specs green; 12 integration tests with a fake clock.
<!-- SECTION:NOTES:END -->
