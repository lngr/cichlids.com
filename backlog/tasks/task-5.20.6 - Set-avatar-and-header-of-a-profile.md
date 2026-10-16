---
id: TASK-5.20.6
title: Set avatar and header of a profile
status: In Review
assignee: []
created_date: '2026-10-16 14:17'
updated_date: '2026-10-16 19:46'
labels: []
dependencies:
  - TASK-5.20.4
references:
  - app/mobile/e2e/playwright/profile-images.write.mjs
parent_task_id: TASK-5.20
priority: low
ordinal: 107000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A member picks an avatar and a header for their profile from their media library or a fresh upload; both are media links with context profile, at most one each (ADR-0029). They are visible to everyone while the profile is active.

### Reference
- ADR-0029, TASK-5.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member with photos in the library, When they choose one as avatar, Then their profile, comments and stories show it
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
- PUT /api/me/avatar and /api/me/header with an own library photo, POST .../photo with a fresh photo (stored in the library, then linked), DELETE removes the link; ProfileMediaService locks profile and file, checks ownership and kind, replaces the link in one transaction and places the files right away; errors media_not_found, profile_media_not_photo, profile_media_invalid, profile_photo_invalid, upload_rate_limited.
- App: profile images screen from Me with previews, library picker, upload, camera (native, cropped 1:1 and 3:1), remove; the profile shows the header.
- Interpretations: no server-side crop (thumb variant with cover fit); a suspended profile hides avatar and header through the visibility rule; setting the linked file again or removing without a link answers OK.
- Fixed while building: the upload answer carried a stale private URL from tracked variants.
- Verification (branch): .NET 641/2078 (1 skipped)/350, client-core 269, mobile 449, 47 write e2e specs green.
<!-- SECTION:NOTES:END -->
