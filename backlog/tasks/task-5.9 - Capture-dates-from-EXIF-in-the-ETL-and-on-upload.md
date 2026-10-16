---
id: TASK-5.9
title: Capture dates from EXIF in the ETL and on upload
status: In Progress
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 04:16'
labels: []
dependencies: []
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
