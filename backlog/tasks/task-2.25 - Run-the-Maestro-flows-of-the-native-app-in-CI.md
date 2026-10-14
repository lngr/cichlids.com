---
id: TASK-2.25
title: Run the Maestro flows of the native app in CI
status: In Review
assignee: []
created_date: '2026-10-09 21:28'
updated_date: '2026-10-14 11:13'
labels: []
milestone: m-0
dependencies: []
references:
  - app/mobile/e2e/maestro/README.md
  - .github/workflows/ci.yml
parent_task_id: TASK-2
priority: medium
ordinal: 50000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Maestro flows in app/mobile/e2e/maestro (browse-gallery, tank-detail, community-thread, login-upload, comment-rating) are written but have never run: the runner blocks on device attach in the devcontainer. ADR-0004 and ADR-0006 name Maestro as the end-to-end tool for the native app. This task makes the flows run on an Android emulator in CI and fixes the flow defects listed under R42 in REVIEW-MVP.md (German UI texts asserted while the app defaults to English, a final assertion that matches any screen, a hardcoded appId).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a push to main, When GitHub Actions runs, Then a job starts an Android emulator, runs all Maestro flows against the stack, and the operator sees them pass in the Actions log
- [ ] #2 Given the emulator locale set to English and then to German, When the flows run, Then they pass in both locales
- [ ] #3 Given a commit that removes the picture detail screen, When the browse-gallery flow runs, Then it fails
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
### Approach
- Fix the flow defects of R42 first, test-first where possible: locale-independent assertions
  (test ids or both languages), a final assertion that proves the target screen, the appId from
  the build configuration instead of a hardcoded value.
- CI job `maestro` on push to main and on dispatch: enable KVM on the ubuntu runner, start the
  compose stack with synthetic data (not the migrated legacy data), build the Android app for the
  emulator (debug build of the staging-free development variant pointing at the stack through
  the emulator host address), start an emulator (pinned system image, reactivecircus
  android-emulator-runner pinned by SHA), run all flows once with locale en and once with de,
  upload the Maestro report.
- AC #3: a documented negative check (for example a flow run against a build without the
  picture detail route in a test branch) instead of a permanent red job.
- Devcontainer: grant the KVM group so the emulator can run locally (operator rebuilds the
  devcontainer); F13 in `PROJECT-STATUS.md` names it.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- Flows: selectors independent of the UI language, synthetic fixtures, a final assertion on the
  picture detail screen, the appId from the app configuration.
- CI job `maestro` (push to main, dispatch, `contents: read`, no secrets): KVM, Android API 35
  `google_apis;x86_64`, Maestro 1.39.0 from the toolchain file, Expo prebuild plus Gradle debug
  APK with embedded JavaScript (no Metro), API through `10.0.2.2`, Keycloak through `adb reverse`
  on `localhost` (shared OIDC issuer), runs with device locale en and de, JUnit reports and logs
  as artifacts. AC #3 as a documented negative check.
- Devcontainer: KVM group access (effective after a rebuild); local run steps in the Maestro README.

### Verified by the agent (Codex)
- Mobile 184 tests and typecheck; 5 flows parse; actionlint; Expo prebuild; Android bundle
  export; fixtures answer 200.
- Not verified: the Gradle APK build and the device runs (first GitHub run); no local emulator.

### Operator checks
- First `maestro` run on GitHub green in both locales (AC #1, #2); the documented negative check
  (AC #3); after a devcontainer rebuild `test -w /dev/kvm` succeeds (F13).
<!-- SECTION:NOTES:END -->
