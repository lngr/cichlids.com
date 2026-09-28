---
id: TASK-3.16
title: Photo upload with variant generation
status: In Progress
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-09-28 14:05'
labels: []
dependencies:
  - TASK-3.10
  - TASK-3.13
  - TASK-3.9
parent_task_id: TASK-3
priority: high
ordinal: 36000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Authenticated photo upload creating a draft post with media item, variant generation into the object store, publish flow, discard of drafts.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an authenticated user on the local stack, When a photo is uploaded and published, Then a post with media item exists, variants are stored in the object store, and the picture appears in the gallery listing
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
Authenticated upload creates a draft post with one photo media item; variants are derived synchronously with the same variant spec the media pipeline uses; the author publishes or discards the draft. New posts mint a generated canonical slug (agreed slug strategy in TASK-3 notes).

### Shared image code
- Move ImageProcessor, MediaVariantSpec and MediaStorageKeys from Cichlids.Etl/Media to Cichlids.Infrastructure/Media (namespace Cichlids.Infrastructure.Media); the NetVips package references move with them. ETL keeps using them through the project reference; existing ETL media tests stay green unchanged apart from namespaces.

### API (Cichlids.Api/Features/Uploads, all RequireAuthorization, profile via CurrentProfileService)
- POST /api/uploads (multipart/form-data, field file): accepts image/jpeg, image/png, image/webp up to 25 MB (400 otherwise, 400 for undecodable data via BrokenImageException). Stores the original at originals/uploads/{profileId}/{uuid}.{ext}, measures orientation corrected width/height, computes SHA-256, derives the variants from MediaVariantSpec.Resolve and stores them under MediaStorageKeys.BuildVariantKey. Creates media_item (owner, kind photo), media_variant rows, post (kind single, state draft, topic cichlids, author) and post_media in one transaction. Returns 201 with the draft (post id, image urls, state).
- GET /api/me/drafts: the caller's drafts, newest first.
- POST /api/posts/{id}/publish with title (required, max 200), description, topic (cichlids, tanks, offtopic): only the author, only from draft; sets published state and published_at, mints a canonical slug_alias via SlugGenerator.GenerateUnique (collision checked against slug_alias), writes outbox event post.published in the same transaction. Returns the picture detail (slug). 404 for foreign or missing posts, 409 when not a draft.
- DELETE /api/posts/{id}: discards a draft of the caller: removes post, post_media, media_item, media_variant rows and the stored objects. 409 for published posts (published deletion is moderation scope).
- Object keys are written before the database commit; a failed commit deletes the written keys (best effort, logged).

### Red-Green order
1. Infrastructure move: build plus existing ETL media tests green (refactor, no behaviour change).
2. Api.Tests UploadsEndpointsTests, red first: upload without token 401; wrong content type 400; broken bytes 400; happy upload returns draft, rows exist, objects exist for original and each variant (widths per spec for a generated 1000x700 JPEG: thumb 200, small 400, medium 800); draft not listed in GET /api/pictures; publish makes it listed with its slug and GET /api/pictures/{slug} works; publish by another profile 404; publish twice 409; discard removes rows and objects.
3. The object store assertions need a real S3: an upload test fixture starts a RustFS container (same image as app/stack/compose.yaml) and points ObjectStorage at it; the existing ApiFixture stays untouched.
4. End-to-end path for the AC: UploadPublishFlowTests (upload, publish, gallery listing, variant objects present) is the story test, referenced via --ref; tagged with the story id.
5. OpenAPI: regenerate app/client-core/openapi.json and the generated client (pnpm run generate), add typed helpers uploads.create, me.drafts, posts.publish, posts.discard with vitest+msw tests; OpenApiDocumentTests stay green.

### Affected files
- app/api/src/Cichlids.Infrastructure/Media/* (moved), Cichlids.Infrastructure.csproj, Cichlids.Etl.csproj and ETL usings
- app/api/src/Cichlids.Api/Features/Uploads/* (endpoints, write service, DTOs), Program.cs (endpoint mapping, form limits)
- app/api/tests/Cichlids.Api.Tests/Features/Uploads/*, an S3-backed fixture
- app/client-core/openapi.json, src/generated, src/client.ts, src/client.test.ts

### Risks and assumptions
- Synchronous variant generation keeps the MVP simple; a 25 MB photo takes about a second with libvips. An async pipeline via the outbox is later work.
- Topic default cichlids, the gallery default lists cichlids and tanks.
- HEIC from iPhones is out of scope (Android first); the client sends JPEG.
- Tank linking and species tagging at publish are out of scope for this story.
<!-- SECTION:PLAN:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
