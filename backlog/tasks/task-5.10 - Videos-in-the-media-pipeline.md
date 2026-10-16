---
id: TASK-5.10
title: Videos in the media pipeline
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 06:28'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/video.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Videos/VideoUploadTests.cs
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

### Length rule placeholder (decided by the project owner, 2026-10-16)
- Members whose account is older than 1 year and who have more than 20 public pictures may upload videos up to 3 minutes; every other member gets the short limit (a separate configuration value, 60 seconds pending confirmation). DRAFT-13 replaces this placeholder with the reputation concept.

- Short limit confirmed by the project owner (2026-10-16): 60 seconds for every member outside the placeholder rule.

### Implementation (2026-10-16)
- Upload: chunked sessions with offsets in Postgres, one S3 multipart part per 8 MiB chunk, any instance accepts the next chunk, oversized or extra bytes and appends after completion are refused; abandoned sessions expire after 24 hours; at most three open sessions per member; the hourly quota counts completed videos. tus was not chosen because its ASP.NET Core servers store on local disk.
- Probe and limits: the file must start with an MP4/QuickTime ftyp box; ffprobe and ffmpeg read only the uploaded local file (-protocol_whitelist file, -f mov, no shell); ffprobe output is capped and every value validated; dimensions 16 to 8192, aspect ratio at most 4:1, long side at most 3840, frame rate capped at 30, first video and audio stream only; the length limit holds on the decoded duration too. Length policy VideoLimitPolicy: placeholder rule (account older than 1 year and more than 20 published pictures: 3 minutes, otherwise 60 seconds), DRAFT-13 replaces it.
- Transcoding: job table claimed with FOR UPDATE SKIP LOCKED and a lease, one video at a time per instance, three attempts, HLS renditions 480p/720p/1080p never above the source, a sharp poster frame, ffmpeg with nice 15, 2 threads and a 20-minute timeout; pod limits cpu 2, memory 1536Mi, a 3Gi work volume; ffmpeg from a digest-pinned static image.
- Attaching: tank videos and care videos; care videos are private and served under a 2-hour token path with relative HLS URIs and an allowlist of file names. Originals stay private.
- App: record or pick, resumable upload with progress, processing state, HLS playback with poster (expo-video native, hls.js or native HLS on the web).
- Security review findings fixed: ffmpeg input confinement, resource caps of the rendition ladder, size caps of the object store, bounds and validation of the ffprobe output.
- Verification: .NET 529/827 (1 opt-in benchmark skipped)/349 with 0 warnings, client-core 131, mobile 368, both typechecks, all 30 write e2e specs green, manifest and image checks, gitleaks clean.
- Tests: app/api/tests/Cichlids.Infrastructure.Tests/Video/*, S3ObjectStoreMultipartTests.cs, app/api/tests/Cichlids.Api.Tests/Features/Videos/*, app/client-core/src/videoUpload.test.ts, app/mobile/e2e/playwright/video.write.mjs.
- Not verified: native playback, Safari native HLS, CORS of the production media bucket for hls.js, pod limits under a real transcode; orphaned multipart uploads after a crash need a bucket lifecycle rule (operator).
<!-- SECTION:NOTES:END -->
