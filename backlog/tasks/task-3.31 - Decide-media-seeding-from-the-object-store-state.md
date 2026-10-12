---
id: TASK-3.31
title: Decide media seeding from the object store state
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-12 06:41'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: high
ordinal: 53000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R20.

The media seed checks every original and variant in the object store (existence and a SHA-256 checksum stored as object metadata) and uploads only missing or differing objects; for present objects it records the metadata in the database. A run after a database reset therefore finishes in minutes, and objects lost from the store are restored.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a fully seeded object store and a freshly reset database, When the operator runs the media seed, Then it finishes within minutes and its log shows no original uploads
- [ ] #2 Given an original the operator deleted from the object store, When the media seed runs, Then the object exists again with the checksum of the legacy file
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
### Approach

- Order: TASK-3.31 lands before TASK-3.29. This task owns the object metadata contract. TASK-3.29 reuses it and bumps the pipeline versions, so the migrated originals are sanitized by the normal seed run with no `--force`.
- Metadata contract in a shared `src/Cichlids.Infrastructure/Media/MediaObjectMetadata.cs`:
  - Keys: `sha256` (checksum of the stored bytes), `source-sha256` (checksum of the legacy source the object was made from), `pipeline` (derivation version), and on photo originals also `width`, `height` and `decodable`.
  - Constants for the original and variant pipeline versions.
  - A decision rule that returns Current, Missing or Differs.
- `IObjectStore`: a new `PutAsync(key, content, contentType, IReadOnlyDictionary<string,string> metadata, ct)` overload, so callers that pass a cancellation token by position keep compiling, and `Task<ObjectHead?> HeadAsync(key, ct)` returning the size and the metadata, or null when the object is missing.
- Originals: for every candidate (`MediaSeedCommand.cs:301-311`), stream-hash the legacy file and HEAD the original key.
  - Current: the object exists and both `source-sha256` and `pipeline` match.
  - Missing or Differs: upload with fresh metadata (width and height measured once).
- Variants: HEAD each label from `MediaVariantSpec.Resolve(width)`.
  - Current: `source-sha256` and `pipeline` match.
  - Otherwise: decode and regenerate.
- Database:
  - `media_item`: size from the HEAD response, `checksum_sha256` from `sha256`, width and height from metadata.
  - `media_variant`: size from the HEAD response, width from the spec.
  - Present objects are never decoded.
- Forum attachments: `ForumMigrationStep` uses the same store check instead of its database byte-size shortcut. After a database reset that shortcut sees no rows and would re-upload every attachment.
- Removed: `--force`, `onlyMissing` and the database-based `IsComplete` shortcut (`MediaSeedCommand.cs:20-28,41,66-69,183-186,218-238`; `Program.cs:87`).
- Objects without metadata (everything seeded before this change) count as Differs. That costs one full re-upload once (about 94 minutes) and avoids guessing.

### Steps

1. Object metadata on put and head (about 0.5 day)
   - Failing tests first in `tests/Cichlids.Infrastructure.Tests/S3ObjectStoreTests.cs` (RustFS):
     - A put with metadata reads back through `HeadAsync` with the size and the metadata, keys without the `x-amz-meta-` prefix.
     - `HeadAsync` on a missing key returns null.
     - This also confirms early that RustFS keeps user metadata.
   - Production change: `src/Cichlids.Infrastructure/Storage/IObjectStore.cs`, and `S3ObjectStore.cs` next to `:27-44` and `:87-104`.
2. Metadata contract and decision rule (about 0.5 day)
   - Failing tests first: unit tests for `MediaObjectMetadata`:
     - building the metadata for an original and for a variant;
     - Decide gives Missing for a null head;
     - Differs on a `source-sha256` mismatch, a `pipeline` mismatch or missing metadata;
     - Current on a full match.
   - Production change: new `src/Cichlids.Infrastructure/Media/MediaObjectMetadata.cs`.
3. Seed summary and isolated databases (about 0.5 day, refactor without behaviour change)
   - Tests first:
     - `MediaEtlFixture.CreateIsolatedTargetAsync()`: one database per test, so the counts are exact.
     - `MediaSeedCommand.SeedAsync` returns a `MediaSeedSummary` with originals uploaded and present, variants uploaded and present, metadata recorded, missing, broken, bytes and elapsed time.
     - Update `MediaTestSupport.RunSeedAsync` and the existing tests.
   - Production change: `src/Cichlids.Etl/Media/MediaSeedCommand.cs`; `RunAsync` prints the summary.
4. Seed decides from the store (about 1 to 1.5 days)
   - Failing tests first in `tests/Cichlids.Etl.Tests/MediaSeedCommandTests.cs`:
     - After a simulated database reset (re-insert the `media_item` rows with new ids, no variant rows), no original and no variant is uploaded, and the database metadata matches the first run.
     - A deleted original is uploaded again; its `source-sha256` equals the SHA-256 of the legacy file.
     - An overwritten original and an object without metadata are re-uploaded.
     - A deleted variant and a variant with an older `pipeline` value are regenerated.
     - A present broken original (`decodable=false`) is recorded without decoding.
     - Rewrite the second-run part of the test at `:15`.
   - Production change:
     - Rewrite `ProcessOneAsync` (`:102-216`) with streamed hashing; bytes are loaded only for an upload or a decode.
     - Remove `IsComplete` and `onlyMissing`.
     - Remove `--force` from `Program.cs:87` and from the command-line allowlist.
     - Raise the `--workers` default if the operator timing shows HEAD requests are the bottleneck.
5. Forum attachments (about 0.5 day)
   - Failing tests first in `tests/Cichlids.Etl.Tests/ForumMigrationStepTests.cs`:
     - A rerun with the attachment present reports 0 uploaded.
     - After the object is deleted, the next run uploads it again with `sha256` and `source-sha256` metadata.
   - Production change: `src/Cichlids.Etl/Steps/ForumMigrationStep.cs` replaces `:366,379,413-421,441,510ff` with the shared store check. The checksum comes from the legacy bytes it already computes.
6. Documentation
   - `MediaSeedCommand.cs:13-29` and `MediaVerifyCommand.cs:8-14`: both describe the database shortcut.
   - `PROJECT-STATUS.md:373`.
   - The `CICHLIDS_MEDIA_LIMIT` row in `app/stack/README.md`.

Size: M, about 3 to 3.5 days in 6 commits, plus one full seed (about 94 minutes) and one timing run.

### Verification

- AC #1:
  - Automated: the database-reset test from step 4 passes.
  - Dev stack:
    1. Run one full seed with `CICHLIDS_MEDIA_LIMIT=0 app/stack/bootstrap.sh`; this writes the metadata.
    2. Run it again.
    3. The media summary shows `originals uploaded: 0` and an elapsed time under 15 minutes.
- AC #2:
  - Automated: the restore test from step 4 passes.
  - Dev stack:
    1. Find a key with `psql -c "select storage_key, legacy_id from media_item where storage_key like 'originals/%' limit 1"`.
    2. Delete it with `amazon/aws-cli ... s3 rm`.
    3. Run `etl media` without `--limit`.
    4. `amazon/aws-cli ... s3api head-object` shows `source-sha256` equal to `sha256sum` of the legacy file. This holds before and after TASK-3.29.

### Risks

- About 800k HEAD requests per full run (179k originals and 623k variants). The run time depends on RustFS throughput under parallel requests; tune `--workers`.
- Every full run reads and hashes about 235 GB of legacy files. On the local NVMe that takes minutes; on staging it depends on where the legacy images live (TASK-2.4).
- There is one full re-upload because existing objects lack metadata, and one more pass when TASK-3.29 bumps the pipeline versions.
- If RustFS drops user metadata, step 1 shows it before the seed is touched.
- TASK-3.29 also changes `IObjectStore.cs` (streamed `GetAsync` with a size cap) and `MediaSeedCommand.cs`. Landing this task first keeps that merge one-directional.
<!-- SECTION:PLAN:END -->
