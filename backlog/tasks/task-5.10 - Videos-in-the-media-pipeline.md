---
id: TASK-5.10
title: Videos in the media pipeline
status: In Progress
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 05:44'
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Limits (decided by the project owner, 2026-10-16)
- Videos up to 3 minutes; HLS renditions up to 1080p (for example 480p, 720p and 1080p, never above the source resolution).
- The original stays stored privately after the transcoding, so videos can be transcoded again later.
- Transcoding runs one video after the other in a queue, so it does not starve the other workloads of the node.

### Open: length by reputation (project owner, 2026-10-16)
- Long videos (up to 3 minutes) are meant only for members with a certain reputation; what reputation is, how it is earned and which length applies to everyone else is to be worked out (PROJECT-STATUS O6). Points and leaderboards are excluded from the app.
- The pipeline resolves the maximum length per member through one policy, so the reputation rule plugs in without changing the upload or transcoding.
<!-- SECTION:NOTES:END -->
