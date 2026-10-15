---
id: TASK-4.3
title: Five-tab navigation Today Discover plus Fish Me
status: In Review
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 09:47'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/navigation.write.mjs
  - app/mobile/e2e/maestro/five-tab-navigation.yaml
  - app/client-core/src/navigation.test.ts
  - app/mobile/src/navigation/adapter.test.ts
parent_task_id: TASK-4
priority: high
ordinal: 74000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The app has the tabs Today, Discover, + (create), Fish and Me. The gallery and the forum archive
move into Discover, the species list into Fish; the navigation between views is owned by XState
machines (ADR-0007).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a signed-in member, When they open the app, Then the five tabs are shown, the gallery and the forum archive are reachable from Discover and the species list from Fish
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Navigation
- Model view states, tab selection, create, history, back, home and external links in shared client-core with pinned XState.
- Project machine targets through a central Expo Router adapter. Translate Android back, native stack pops, browser history and incoming links into machine events.
- Preserve existing URLs. Keep one instance of each nested list and dismiss to machine targets after upload or back.
### Interface
- Today placeholder; Discover archive entry points and gated activity; photo-only create sheet; Fish species list; existing Me content.
- Translate labels in all eleven catalogs and synchronize system appearance through the theme subscription.
### Verification
- Unit-test machine and platform adapter transitions. Prove AC #1 with a signed-in TASK-4.3 Playwright write spec and maintain Maestro flows.
- Run client-core/mobile tests and typechecks, the isolated write suite, combined .NET build/tests and the story binding gate.
- Keep API and tank forms outside this task.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- XState 5.33.2 owns view states, tabs, create, history and upload destinations in shared client-core.
- A central adapter binds Expo Router, Android back, native pop gestures, browser history and links. JavaScript stacks use unique list identities; back and publish dismiss to the machine target.
- Discover provides archive entry points for gallery, tanks and forum, plus the gated activity slot. Fish embeds species browsing. Today is a placeholder; the create sheet offers photo upload; Me preserves its existing content.
- Existing URLs are valid. All eleven catalogs include the navigation texts. Appearance changes update the tab colors through a synchronized theme subscription.
### Red observations
- Machine tests failed on the missing dependency/module; the signed-in E2E path failed on today-placeholder.
- Adapter tests failed on missing URL normalization, browser sheet dismissal and dismiss projection.
- Write regressions exposed discard navigation, duplicate gallery instances and stale tab colors after a system appearance change. The corresponding paths pass.
### Verified
- client-core: pnpm test (22 tests), pnpm typecheck and build pass.
- mobile: pnpm test (226 tests) and pnpm typecheck pass; the catalog test covers all eleven languages.
- Combined API: dotnet build --no-restore, 0 warnings, 0 errors; dotnet test --no-build, 1110 tests passed.
- flock /tmp/cichlids-e2e-write.lock pnpm run e2e:write: all 16 specs passed, including navigation, upload, session, legacy redirects and localization/RTL.
- node tools/check-story-test-binding.mjs passes; the Android runner passes bash syntax validation.
### Cleanup and limits
- The isolated API/web servers stopped; test database and bucket were removed. Dev-user offline sessions were revoked; dev-user and dev-mod each have zero remaining offline sessions for the app clients.
- Maestro/native device execution is unavailable because adb and Maestro are absent. The read-only legacy smoke and remote CI were not executed. No push was performed; the CI completion item is unchecked.
<!-- SECTION:NOTES:END -->
