---
id: TASK-3.29
title: Keep media originals private and strip location metadata
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: high
ordinal: 51000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R1, R23, R24.

Originals are never publicly readable; the bucket policy exposes variants and forum attachments only, and the API returns no original URLs. Every stored original (new uploads and, once, the migrated legacy originals) loses GPS data, camera serial numbers and owner tags, while capture date, orientation, colour profile and camera model stay. Variants are converted to sRGB before their metadata is removed. The 934 undecodable legacy files get a sanitized public copy during the media seed. The image component itself enforces the pixel limit and a format allowlist for every caller, decodes a photo once for all variants, and object downloads are streamed with a configurable size cap.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an uploaded phone photo with GPS tags, When the operator requests its original object URL without credentials, Then the object store refuses the request
- [ ] #2 Given that photo, When the operator downloads the stored original with operator credentials and inspects it with exiftool, Then GPS, serial number and owner tags are absent while capture date, orientation and colour profile are present
- [ ] #3 Given a migrated legacy original that had GPS tags in the backup, When the operator inspects the stored object after the seed, Then the GPS tags are absent
- [ ] #4 Given a Display P3 photo, When the operator compares its medium variant with the original in a colour-managed viewer, Then the colours match and the variant carries no metadata
- [ ] #5 Given an image file of a few kilobytes that declares 200000 x 200000 pixels, When it is uploaded or passed to the media seed, Then it is rejected and the process memory stays bounded
- [ ] #6 Given one of the undecodable legacy pictures, When the app shows it, Then the image loads from a public sanitized copy
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
