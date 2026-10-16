---
id: TASK-5.20.4
title: Media library of a member
status: In Review
assignee: []
created_date: '2026-10-16 14:17'
updated_date: '2026-10-16 19:19'
labels: []
dependencies:
  - TASK-5.20.2
references:
  - app/mobile/e2e/playwright/media-library.write.mjs
  - >-
    app/api/tests/Cichlids.Api.Tests/Features/Library/MediaLibrarySecurityTests.cs
parent_task_id: TASK-5.20
priority: medium
ordinal: 105000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Every member has a media library with all files they own, linked or not (decided by the project owner, 2026-10-16). A fresh upload can sit in the library until it is linked, and a file stays there when its last link goes away. Only deleting a file from the library removes it with its renditions and all of its links, after a confirmation that names the places that show it. Files without links are visible to the owner only.

### Reference
- ADR-0029, TASK-5.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member whose care entry with a photo is deleted, When they open their media library, Then the photo is still there
- [ ] #2 Given a photo in a tank section and a story, When the owner deletes it from the library and confirms, Then it disappears from both and its files are removed
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
- Removing a link keeps the file (care photos, care entries, care and tank videos, water change cancel and expiry, draft discard and expiry); placement runs after the removal.
- /api/me/media: list (paged, kind, linked or unlinked), detail with places and keepsStreak (tank and ISO week the file keeps), DELETE (file, renditions, HLS objects and links in one transaction, objects after commit), POST /photos into the library; other members get media_not_found.
- used_checksum records photo checksums used as proof per member and kind, so removing a link or deleting a file never makes a picture usable as proof; backfilled by the migration.
- App: My media under Me with grid, filters, detail sheet with places and streak note, delete confirmation naming places and the streak effect, upload.
- Interpretations: journal F22 (a published story losing its last item is removed, a draft deleted; decommission proofs answer 409 media_is_proof).
- Verification (branch on main): .NET 641/2066 (1 skipped)/350, client-core 266, mobile 447, 46 write e2e specs green.
<!-- SECTION:NOTES:END -->
