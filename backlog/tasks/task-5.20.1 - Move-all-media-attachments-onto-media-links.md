---
id: TASK-5.20.1
title: Move all media attachments onto media links
status: In Review
assignee: []
created_date: '2026-10-16 14:10'
updated_date: '2026-10-16 16:04'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/media-links.write.mjs
  - >-
    app/api/tests/Cichlids.Infrastructure.Tests/Persistence/MediaLinkMigrationTests.cs
parent_task_id: TASK-5.20
priority: high
ordinal: 101000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
One table media_link attaches media items to tanks, inhabitants, care entries, water change sessions, gallery entries, discussion posts and profiles (ADR-0029). Provenance moves to the media item. All existing attachments move with their order; every API response stays as it is.

### Reference
- ADR-0029, TASK-5.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a database with attachments in every context, When the migration runs, Then every attachment exists as a media link with its context, role, position and provenance, and the API returns the same tanks, care entries, water change sessions, pictures, forum posts and profiles as before
- [ ] #2 Given a link to a context, When that context is deleted, Then its links are deleted with it and the media items stay
- [ ] #3 Given the legacy import, When it runs against an empty target, Then it writes media links in the same shape
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Schema
- media_link: id, media_item_id, context_type (tank, inhabitant, care_entry, water_change_session, post, discussion_post, profile), one nullable FK per context with ON DELETE CASCADE, check that exactly the key of context_type is set, role (text with check per context), position, caption (nullable), created_at. Unique (context, role, media_item); unique (context, role, position) where ordered; unique (profile, role) for avatar and header.
- Roles: tank main, showcase, decoration, technic, time_lapse, video; care_entry photo, video; water_change_session evidence; post item; discussion_post attachment; profile avatar, header. inhabitant gets the column and the check, no role yet.
- media_item: captured_in_app (bool), received_at (timestamptz, the moment the server stored the upload; backfilled from created_at).

### Data move (one EF migration with SQL)
- tank_media (section to role, sort to position, captured_in_app to media_item) and tank.main_media_id (role main).
- post_media (role item); post.tank_id and post_species stay until TASK-5.20.3.
- care_photo (role photo, position), care_video (role video, position by id).
- water_change_session_photo (role evidence, position; captured_in_app and received_at to media_item).
- discussion_post_media (role attachment, sort).
- profile.avatar_media_id and profile_image_media_id (roles avatar and header).
- Drop the old tables and columns in the same migration; Down restores them.

### Code
- Writers: TankSeriesPhotoService, VideoUploadService, VideoRemovalService, UploadsWriteService, CarePhotoService, CareWriteService, WaterChangeService (photos and evidence query), WaterChangeCleanupHostedService, DraftCleanupService.
- Readers: TanksQueryService, TankTimeLapseQueryService, TankMediaEndpoints, DiscoverQueryService, VideoQueryService, VideoTranscodingService, StreakService (time-lapse photos), PicturesQueryService, DraftsQueryService, TodayQueryService, CareEntryMapper, CarePhotoEndpoints, CareEvents, CommunityQueryService, ProfilesQueryService, AuthorBatchLoader, TankMediaPlacementService (movable items).
- Evidence: captured_in_app and received_at come from the media item; reuse means the checksum appears in an evidence link of another finished session of the same owner.
- Limits stay per context (6 photos and 2 videos per care entry, 6 evidence photos).
- ETL: PictureMigrationStep, ForumMigrationStep, VerifyStep and MigrationRules write and verify media_link; tools/golden-snapshot/fixture-schema.sql follows the schema.
- HTTP contracts stay unchanged. Care photo deletion addresses the link id where it addressed the care_photo id.
- Tests: SeedData, ApiFixture, UploadsFixture, CareTestSupport, TestDataBuilder build links; a migration test covers every context.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation (2026-10-16)
- media_link with typed nullable keys, context type, exactly-one check, role check per context, position, caption; unique indexes per context rule, one main image per tank, one avatar and one header per profile; media_item.captured_in_app and received_at.
- Migration 20261016155023_MediaLinks moves tank sections, main images, decommission and recommission proofs, post items, care photos and videos, water change evidence, discussion attachments and profile avatar and header; Down restores them.
- Evidence reuse: same checksum in an evidence link of another finished session or an earlier proof of the member. Care photo deletion addresses the link id; HTTP contracts unchanged.
- Interpretations: tank and care videos share the role video; received_at backfilled from created_at (evidence photos from the recorded session time); captured_in_app true when any former attachment was flagged; no database cap for care videos.
- Verification (branch on main): .NET 570/1336 (1 skipped)/349, client-core 253, mobile 430, 41 write e2e specs green. Golden snapshot follows in TASK-5.22.
<!-- SECTION:NOTES:END -->
