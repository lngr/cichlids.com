---
id: TASK-4.1
title: Feature flags in API and app
status: In Review
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 09:01'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/00-feature-flags.write.mjs
  - app/api/tests/Cichlids.Api.Tests/FeatureFlagsTests.cs
  - app/mobile/src/featureFlags/featureFlagStore.test.ts
parent_task_id: TASK-4
priority: high
ordinal: 72000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Features that only make sense with enough activity ship finished but switched off. Flags live in
the API configuration per environment and reach the app through an endpoint; the app hides
flagged sections while a flag is off.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the flag discover-activity switched off in an environment, When a member opens Discover, Then the activity sections are absent, and after switching the flag on they appear without a new app build
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
### Implemented
- API: `FeatureFlags:<name>` per environment, known names only (`discover-activity`), booleans
  only, validated at start; anonymous `GET /api/feature-flags` (names and booleans, cache 30 s);
  client in client-core regenerated; staging and prod set the flags off.
- App: flag store loaded at start and on return to the foreground, `useFeatureFlag(name)`, every
  flag off when the endpoint fails; the Discover activity placeholder is gated.

### Verified by the agent (Codex) and the main session
- Red first (404, missing validation, client method, store); .NET 1,101 tests; client-core 15;
  mobile 215; write suite 13 of 13 including `00-feature-flags.write.mjs`.
- To repeat after TASK-4.2: the full write suite on the combined state.
<!-- SECTION:NOTES:END -->
