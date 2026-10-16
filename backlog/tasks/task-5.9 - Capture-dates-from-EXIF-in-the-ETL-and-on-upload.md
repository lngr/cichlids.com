---
id: TASK-5.9
title: Capture dates from EXIF in the ETL and on upload
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 04:37'
labels: []
dependencies: []
references:
  - app/api/tests/Cichlids.Etl.Tests/MediaCaptureTimeCommandTests.cs
  - app/mobile/e2e/playwright/tank-story.write.mjs
parent_task_id: TASK-5
priority: medium
ordinal: 89000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Media items store the capture date from EXIF, filled by the ETL for the archive and by the upload pipeline; time-lapses and On this day prefer it over the publication date.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an archive photo with an EXIF capture date, When the ETL migrates it, Then the media item stores the capture date and the time-lapse orders by it
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
- media_item stores the capture time as the camera's wall-clock time (taken_at_local, timestamp without time zone) and the offset in minutes when the camera recorded one (taken_at_offset_minutes; null means unknown zone, not UTC), plus capture_checked_at for examined originals. Only DateTimeOriginal and OffsetTimeOriginal are read through libvips; GPS and other EXIF fields are never read. The zero date, dates before 1990, dates more than a day ahead and malformed values are ignored.
- Gallery uploads and care photos store the capture time from the original before the metadata is stripped.
- ETL: etl media runs the capture-time pass after seeding; media-dates runs it on its own, incrementally (only unexamined rows; rows with a missing source file stay candidates), reading the first 256 KB of each legacy original and the whole file only when the header needs it; it reports candidates, examined, found, no date, implausible, unreadable, source missing and bytes read. It reads the legacy originals and the legacy MySQL; the operator command is in app/stack/README.md, section Capture times of photos.
- Consumers: the time-lapse orders by the capture time with the publication or upload date as fallback, the showcase rule uses the same effective date, On this day uses the capture day when present.
- Verification: .NET 352/685 (1 opt-in benchmark skipped)/349 with 0 warnings, client-core 82, mobile 271, both typechecks, all 26 write e2e specs green.
- Tests: app/api/tests/Cichlids.Infrastructure.Tests/CaptureTimeReaderTests.cs, app/api/tests/Cichlids.Etl.Tests/MediaCaptureTimeCommandTests.cs, app/api/tests/Cichlids.Api.Tests/Features/Tanks/TankTimeLapseTests.cs, TodayTests.cs, UploadsEndpointsTests.cs, CarePhotoTests.cs, app/mobile/e2e/playwright/tank-story.write.mjs.
- Not verified: the real archive (maker quirks, large metadata blocks); the backfill has not run against the dev database or the archive.
<!-- SECTION:NOTES:END -->
