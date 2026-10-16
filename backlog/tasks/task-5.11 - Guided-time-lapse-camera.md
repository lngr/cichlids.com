---
id: TASK-5.11
title: Guided time-lapse camera
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 05:44'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/time-lapse-camera.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Tanks/TankSeriesPhotoTests.cs
parent_task_id: TASK-5
priority: low
ordinal: 91000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The camera shows the last photo of the tank half-transparent over the viewfinder, so every photo has the same framing; new photos extend the time-lapse of TASK-4.6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a tank with photos, When the member opens the time-lapse camera, Then the last photo appears half-transparent over the viewfinder and the new photo joins the series
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
- API: POST /api/tanks/{id}/time-lapse/photos (owner only) stores a photo through the shared upload pipeline (formats, limits, EXIF capture time, public variants) as tank media of the new time_lapse section, without a gallery post; outbox tank.photo_added in the same transaction; coded refusals tank_not_found, tank_photo_invalid, upload_rate_limited, tank_photo_auth_required. The time_lapse section stays out of the showcase and the main-image fallback.
- App: Time-lapse photo on the own tank page opens /tanks/{id}/camera with expo-camera: the newest series photo half-transparent over the viewfinder (40 percent by default, slider, hide toggle), review with retake, upload, return to the tank page. Permission explained first, calm hint when denied; the library picker always works with a comparison preview. Browser shots are JPEG and carry no EXIF, so they sort by upload time.
- Verification on the combined state with TASK-5.12: .NET 352/782 (1 opt-in benchmark skipped)/349 with 0 warnings, client-core 115, mobile 340, both typechecks, all 29 write e2e specs green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Tanks/TankSeriesPhotoTests.cs, app/client-core/src/timeLapseCamera.test.ts, app/mobile/e2e/playwright/time-lapse-camera.write.mjs.
- Not verified: expo-camera on a device, the settings link and the iOS camera usage text, EXIF on native shots, screen reader actions of the slider.
<!-- SECTION:NOTES:END -->
