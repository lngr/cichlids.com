---
id: TASK-5.10
title: Videos in the media pipeline
status: To Do
assignee: []
created_date: '2026-10-15 12:26'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 90000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Videos upload resumably, are transcoded to HLS with ffmpeg, get a poster frame and are delivered from Hetzner within length and resolution limits.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who uploads a short video to a tank, When the processing is done, Then the tank timeline plays the video as HLS with a poster frame
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
