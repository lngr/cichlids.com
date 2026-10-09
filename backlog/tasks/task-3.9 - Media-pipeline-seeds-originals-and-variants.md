---
id: TASK-3.9
title: Media pipeline seeds originals and variants
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-09 02:35'
labels: []
dependencies:
  - TASK-3.5
parent_task_id: TASK-3
priority: high
ordinal: 29000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Originals from the legacy image tree into the object store under stable keys with filename normalization; variant generation per the agreed variant set; verification of database references against stored objects.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given migrated media rows and the legacy image files, When the media pipeline runs, Then originals and the agreed variants exist in the object store under stable keys and a verification pass reports referenced but missing objects
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
1. Thin object storage abstraction (S3 API only, provider as configuration) in a new Cichlids.Infrastructure project, used by media pipeline and upload.
2. Integration tests run the store via Testcontainers.
3. Variant generation and the full legacy seed follow once the variant set and the media key scheme are decided.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Full seed complete: 179337 of 179340 media items in the object store (3 source files missing), 623220 variants (200/400/800/1600, no upscaling), 283 GB transferred in 94 minutes, idempotent restarts proven; 934 legacy files are not decodable and keep original-only delivery, list saved as session artifact. Public read verified anonymously.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
