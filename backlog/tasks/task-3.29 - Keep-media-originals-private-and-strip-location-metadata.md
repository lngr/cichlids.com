---
id: TASK-3.29
title: Keep media originals private and strip location metadata
status: In Progress
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-12 09:01'
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

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach

#### Precondition
- TASK-3.31 is Done. It delivers:
  - IObjectStore.PutAsync with metadata and HeadAsync (size plus metadata).
  - MediaObjectMetadata with the keys sha256 (stored bytes), source-sha256 (input bytes), pipeline (derivation version), and width, height and decodable on originals.
  - MediaVariantSpec.PipelineVersion for variants and an original pipeline version constant.
  - A seed that decides from object store state.
  - Removal of --force and of the DB IsComplete shortcut.
- This task reuses that contract and adds no new metadata keys.
- File and line references below describe main before TASK-3.31; re-locate them after rebasing.

#### Current state
- **Bucket policy:** S3ObjectStore.EnsurePublicReadPolicyAsync (app/api/src/Cichlids.Infrastructure/Storage/S3ObjectStore.cs:167-202) grants anonymous s3:GetObject on bucket/*. The ETL applies it on every run (app/api/src/Cichlids.Etl/Program.cs:203), and app/mobile/e2e/bucket-tool.cs:58-75 repeats it.
- **Original URLs in the API:**
  - MediaUrlBuilder (app/api/src/Cichlids.Api/Features/Media/MediaUrlBuilder.cs:18) returns the original URL in ImageUrlsDto.Original (ImageUrlsDto.cs:9).
  - ProfilesQueryService.cs:37-48 returns the original of the profile image.
- **Upload storage:** uploads store the raw bytes (UploadsWriteService.cs:60), and size and checksum describe those raw bytes (:79-82).
- **Repeated decoding:** ImageProcessor decodes the source again for every call: 1 + N times per upload (UploadsWriteService.cs:42-54) and the same in the seed (MediaSeedCommand.cs:176-193).
- **Variant metadata and colour:** variants use strip without colour conversion (ImageProcessor.cs:47).
- **Pixel limit and formats:** the pixel limit exists only in the upload service (UploadsWriteService.cs:29,43). Every libvips loader is reachable; NetVips.Native 8.16.1 ships the SVG (rsvg) and HEIF loaders.
- **Downloads:** S3ObjectStore.GetAsync (:46-66) buffers the whole object in memory.
- **Legacy data** (read-only scan of /workspaces/legacy-data/images/userpics):
  - In a 3000-file JPEG sample: about 6 percent have a GPS IFD pointer (about 11k files overall), about 10 percent an ICC profile, about 22 percent XMP, about 6 percent APP13.
  - By content: 819 BMP files (302 of them named .jpg), 91 empty files, about 106 files that are not images.
  - The bundled libvips has no BMP loader, so the 934 undecodable files are mostly BMPs. Browsers, Android and iOS render BMP.

#### Mechanism
1. **Bucket policy:** anonymous s3:GetObject on variants/* and forum_attachments/* only. The public prefixes are defined once in MediaStorageKeys and used by the ETL, the test fixtures and bucket-tool.cs.
2. **Downloads:** GetAsync streams the response. ObjectStorage:MaxDownloadBytes is enforced by a Content-Length pre-check plus a capped read stream. Default 64 MiB: the largest legacy file is 20.5 MB and the upload cap is 25 MiB.
3. **Image component** (Cichlids.Infrastructure/Media), single entry point ImageProcessor.Process:
   - **Format allowlist:** JPEG, PNG, WebP, GIF and TIFF. Enforced globally by blocking all other libvips foreign loaders through the NetVips operation-blocking API (check the member names against NetVips 3.1.0), and per call by checking the sniffed loader (UnsupportedImageFormatException).
   - **Pixel limit:** 100 MP, checked against header dimensions before any decode (ImageTooLargeException). The operation cache is off.
   - **Decode once:** one ThumbnailSource or ThumbnailBuffer call to the largest resolved variant width (size Down, auto-rotate, exportProfile srgb). Embedded ICC profiles are converted to sRGB, and untagged CMYK uses the built-in CMYK profile. The result is materialised in memory, and the smaller variants are derived from it.
   - **Encoding:** JPEG Q82 with keep None, so no EXIF, XMP, IPTC or ICC. The result is untagged sRGB.
4. **MediaMetadataSanitizer:** in-house, lossless container filters in C# that never touch encoded image data. Reasons: memory-safe parsing of untrusted uploads; libvips re-encodes on save; exiftool would add a Perl runtime and has a history of code execution on crafted input (CVE-2021-22204).
   - **JPEG:** keeps APP0 JFIF, APP2 ICC_PROFILE chunks, APP14 Adobe, and all tables, frames and scans. Drops APP1 XMP and extended XMP, APP13 (IPTC), COM, APP2 MPF/FPXR, other APPn, and every byte after the primary EOI (MPF secondary images and gain maps carry their own EXIF).
   - **EXIF/TIFF IFD rule:** in-place compaction plus zeroing of the value bytes of dropped entries.
     - IFD0 keeps Make, Model, Orientation, X/YResolution, ResolutionUnit, Software, DateTime, YCbCrPositioning, Artist, Copyright and the Exif pointer.
     - The Exif IFD keeps DateTimeOriginal, DateTimeDigitized, OffsetTime*, SubSecTime*, exposure and lens model data, ColorSpace, PixelX/YDimension and the Interop pointer.
     - The Interop IFD is kept; it is needed to read the colour space of AdobeRGB DCF files.
     - Removed: the GPS IFD, MakerNote, BodySerialNumber, LensSerialNumber, CameraOwnerName, ImageUniqueID, UserComment, ImageDescription, HostComputer, the XP* tags, and IFD1 with its thumbnail.
     - EXIF that cannot be parsed is dropped as a whole.
   - **PNG:** eXIf through the IFD rule (CRC recomputed). tEXt, zTXt, iTXt and tIME are dropped. iCCP, sRGB, gAMA, cHRM, cICP, pHYs and APNG chunks are kept.
   - **WebP:** the EXIF chunk goes through the IFD rule. The XMP chunk is dropped, and the VP8X flags and RIFF size are fixed. ICCP is kept.
   - **GIF:** comment and XMP application extensions are dropped. NETSCAPE2.0 and ICCRGBG1 are kept.
   - **TIFF** (about 38 legacy files): re-encoded pixel-identical by libvips with keep Icc.
   - **BMP:** passed through; BMP has no EXIF, XMP or GPS container.
   - **Unknown formats and videos:** passed through and stay private.
5. **Pipeline versions:** sanitizing becomes part of the original pipeline. Bump the original pipeline version constant (sanitizer) and MediaVariantSpec.PipelineVersion (sRGB conversion). The next normal seed (TASK-3.31 store-state decision) then replaces unsanitized originals and regenerates all variants. No separate retroactive pass and no separate sanitizer key.
6. **API:**
   - ImageUrlsDto.Original is removed.
   - MediaUrlBuilder builds URLs from variant keys only and throws on keys outside the public prefixes.
   - Profile images use their best variant.
   - A media item that has only the public copy (variant label copy) resolves all four sizes to the copy, so the client contract is unchanged.
7. **Seed and forum ETL:**
   - Originals are sanitized before upload, with sha256 of the stored bytes and source-sha256 of the input.
   - Undecodable but browser-renderable files (sniffed as JPEG, PNG, GIF, WebP or BMP, BMP header checked against the pixel limit) get a sanitized public copy at variants/copy/{originalKey}. It has a sniffed content type, pipeline metadata, and a media_variant row with label copy.
   - Pixel bombs are rejected: nothing is uploaded and the case is reported.
8. **Comments:** C# doc comments follow R43 (no inline tags) and describe current behaviour only. The R45 comments at ImageProcessor.cs:55-58 and S3ObjectStore.cs:168 disappear with the rewrite.

### Steps

Each step is one conventional commit: red test first, then the production change.

1. **feat(storage): allow anonymous reads of variants and forum attachments only**
   - Red: in app/api/tests/Cichlids.Infrastructure.Tests/S3ObjectStoreTests.cs:126-155, the anonymous GET on originals/public-read.jpg must return 403. New tests: variants/x.jpg and forum_attachments/x.jpg return 200; a missing variant returns 404.
   - Green:
     - EnsurePublicReadPolicyAsync(publicPrefixes) with prefixed resource ARNs and a corrected doc comment.
     - MediaStorageKeys.PublicPrefixes.
     - Etl/Program.cs passes the prefixes.
     - bucket-tool.cs uses the same statement.
     - UploadsFixture applies the policy.
   - Runs first because it is the cheapest check that RustFS 1.0.0-beta.8 honours prefix resources.

2. **feat(storage): stream object downloads with a configurable size cap**
   - Red (RustFS, S3ObjectStoreTests):
     - GetAsync returns a non-seekable stream with the same content.
     - With the cap set to 1 KiB, an object one byte over throws ObjectTooLargeException; exactly the cap succeeds.
     - Unit test: the capped read stream throws past the cap even if Content-Length lied.
   - Green:
     - S3ObjectStore.GetAsync returns a stream that owns the GetObjectResponse.
     - S3ObjectStoreOptions.MaxDownloadBytes (default 64 MiB, positive).
     - The ETL binds ObjectStorage with Get<S3ObjectStoreOptions>().
     - Callers (seed forum-attachment read, MediaTestSupport.DownloadAsync) adjusted.

3. **feat(media): enforce the format allowlist and pixel limit inside the image component**
   - Red: new ImageProcessorTests.
     - SVG, HEIF/AVIF, PDF and BMP bytes throw UnsupportedImageFormatException.
     - A test-built PNG of a few KB whose IHDR declares 200000 x 200000 throws ImageTooLargeException within 1 s, with the libvips tracked-memory high-water mark under 64 MB.
     - Image.NewFromBuffer on SVG fails after the processor's static setup.
     - UploadsEndpointsTests: that PNG uploaded as image/png returns 400 and stores no object or row.
   - Green:
     - Static setup in ImageProcessor (blocked loaders, cache off), MaxPixels, format detection.
     - Both exceptions move into the component (from UploadsWriteService.cs:179-182).
     - UploadsEndpoints maps both to 400.

4. **feat(media): decode once and convert variants to sRGB before removing metadata**
   - Red (ImageProcessorTests):
     - A P3-tagged JPEG (libvips built-in p3 profile if 8.16 provides it, otherwise a committed CC0 compact Display P3 profile) with an in-gamut colour: the medium variant pixel matches the analytically computed sRGB value (P3 to XYZ D65 to sRGB, tolerance 3 per channel).
     - The variant has no icc-profile-data, exif-data or xmp-data fields, and MetadataExtractor finds no Exif or ICC directory.
     - A CMYK JPEG yields RGB variants.
     - Process over a counting stream reads at most source length plus 64 KiB.
   - Green:
     - ImageProcessor.Process returns format, width, height and variants.
     - Measure, GenerateVariant and LoadOriented are removed.
     - Upload and seed call Process.
     - Bump MediaVariantSpec.PipelineVersion.

5. **feat(media): remove location, serial and owner metadata from originals losslessly**
   - Red: MediaMetadataSanitizerTests. Fixtures come from a test-only EXIF builder in a shared test source file linked into the three test projects; the independent oracle is MetadataExtractor (test-only).
     - JPEG with GPS, BodySerialNumber, LensSerialNumber, CameraOwnerName, MakerNote, XMP with GPS, APP13, COM and a trailing MPF image with GPS: all absent afterwards.
     - DateTimeOriginal, Orientation, ICC, Model, Artist and Copyright are still present.
     - The bytes from SOF0 to EOI are identical; the sanitizer is idempotent.
     - A truncated or looping IFD drops the EXIF block without throwing.
     - Same checks for PNG eXIf and iTXt, WebP EXIF and XMP, and GIF comments.
     - TIFF decodes to identical pixels without GPS.
     - BMP and unknown bytes come back unchanged.
   - Green: MediaMetadataSanitizer plus JpegMetadataFilter, PngMetadataFilter, WebpMetadataFilter, GifMetadataFilter and ExifIfdScrubber. Bump the original pipeline version constant.

6. **feat(uploads): store sanitized originals with the object metadata contract**
   - Red (UploadsEndpointsTests, GPS JPEG fixture):
     - The stored original has no GPS, serial or owner tags and keeps capture date, orientation and ICC.
     - media_item.byte_size and checksum_sha256 match the stored bytes (update the asserts at :166 and :169).
     - The object metadata carries sha256, source-sha256, pipeline, width, height and decodable.
     - Anonymous GET on the original returns 403, and on the medium variant returns 200.
   - Green: UploadsWriteService.CreateDraftAsync calls Process, then Sanitize, then PutAsync with metadata.

7. **feat(api)!: return no original image URLs**
   - Red:
     - New PublicResponsesExposeNoOriginalsTests: the picture list and detail, tank list and detail, community thread, profile and drafts contain no /originals/ and no original property.
     - ProfilesEndpointsTests: profileImageUrl is a variant URL.
     - Update the asserts at PicturesEndpointsTests.cs:154, TanksEndpointsTests.cs:64,75, CommunityEndpointsTests.cs:191 and UploadsEndpointsTests.cs:144.
     - vitest app/mobile/src/upload/drafts.test.ts:54-66: without variants both helpers return null.
   - Green:
     - ImageUrlsDto without Original.
     - MediaUrlBuilder.Build(variantKeysByLabel) with the public-prefix guard and a best-variant helper.
     - Remove the original lookups in the Pictures, Tanks, Community and Drafts query services.
     - The profile image uses the best variant.
     - Regenerate app/client-core/openapi.json and src/generated, and update the client.test.ts fixtures.
     - drafts.ts returns string or null; DraftsSection.tsx and app/upload/index.tsx show the placeholder.
     - API and clients go in one commit.

8. **feat(etl): sanitize forum attachments before storing them**
   - Red (ForumMigrationStepTests): a JPEG attachment with GPS is stored without GPS and with the metadata contract; a non-image attachment is stored byte-identical.
   - Green: the ForumMigrationStep attachment upload sanitizes recognised image containers and writes the metadata.

9. **feat(etl): seed sanitized originals and sRGB variants through the pipeline versions**
   - Red (MediaSeedCommandTests):
     - A legacy JPEG with GPS is stored without GPS and keeps date, orientation and ICC.
     - An original and variants already in the store under the previous pipeline versions are replaced on a normal seed run.
     - A legacy 200000 x 200000 PNG is reported as rejected with nothing uploaded.
     - A legacy file over the size cap is rejected.
     - P3 legacy variants come out in sRGB.
   - Green: the seed calls Process, then Sanitize, checks the file size before reading, and adds rejected counts and entries to the report.

10. **feat(etl): give undecodable legacy pictures a public sanitized copy**
    - Red:
      - MediaSeedCommandTests: a BMP named .jpg yields variants/copy/{key} with content type image/bmp, anonymous GET 200, a media_variant row with label copy and the width from the BMP header, and decodable false on the original.
      - An empty file and a non-image yield no copy and are reported.
      - A second run over the copy-only item uploads nothing.
      - New MediaUrlBuilderTests (Api.Tests): a copy-only item resolves all four sizes to the copy; sized variants win over the copy.
    - Green:
      - MediaStorageKeys.BuildPublicCopyKey and MediaVariantSpec.PublicCopyLabel.
      - A sniffer for browser-renderable formats.
      - The copy branch in the store-state decision (an undecodable original expects exactly the copy object).
      - copies in the missing report.
      - The fill-in rule in MediaUrlBuilder.

11. **test(e2e): cover private originals and legacy copies on the write stack**
    - Red, then green: new app/mobile/e2e/playwright/media-privacy.write.mjs with "// Story: TASK-3.29".
      - Log in and upload the committed synthetic e2e/fixtures/gps-photo.jpg (fake coordinates).
      - The preview loads, and the draft JSON has no original.
      - The original URL, derived from the thumb URL (variants/thumb/{originalKey}.jpg), returns 403 to an anonymous fetch while the variant returns 200.
      - Seed a copy-only legacy picture into cichlids_e2e (the setDraftTexts precedent in upload.write.mjs:25-39) and put e2e/fixtures/legacy-undecodable.bmp through a new bucket-tool.cs put action restricted to cichlids-e2e.
      - /gallery/{slug} shows the image with naturalWidth above 0 and a src under /variants/copy/.
    - Reference the spec from the task.

12. **docs: describe private originals and the operator checks**
    - Amend ADR-0009: the serving store holds variants, forum attachments and public copies; originals stay private in the master store. Video originals stay private and are not delivered; stripping video location metadata belongs to the ADR-0012 pipeline.
    - app/stack/README.md: the policy, the operator download through the amazon/aws-cli container, and exiftool.
    - PROJECT-STATUS.md: the 934 files.
    - Add libimage-exiftool-perl to .devcontainer/Dockerfile.

**Size: L** (12 commits).

### Verification

- **AC1 (original refused without credentials)**
  - Automated: steps 1, 6 and 11.
  - Operator:
    - Get the newest upload key: docker compose -f app/stack/compose.yaml exec postgres psql -U cichlids -d cichlids -c "select storage_key from media_item where storage_key like 'originals/uploads/%' order by id desc limit 1"
    - curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:9000/cichlids-media/KEY gives 403; variants/medium/KEY.jpg gives 200.
- **AC2 (stored original has no GPS, serial or owner tags but keeps date, orientation, colour profile)**
  - Automated: steps 5 and 6.
  - Operator:
    - docker run --rm --network host -e AWS_ACCESS_KEY_ID="$RUSTFS_ACCESS" -e AWS_SECRET_ACCESS_KEY="$RUSTFS_SECRET" -v "$PWD:/w" amazon/aws-cli --endpoint-url http://127.0.0.1:9000 s3 cp s3://cichlids-media/KEY /w/original.jpg (RUSTFS_ACCESS and RUSTFS_SECRET hold the RustFS keys from app/stack/compose.yaml.)
    - exiftool -a -G1 -s original.jpg | grep -Ei 'gps|serial|owner' prints nothing.
    - exiftool -DateTimeOriginal -Orientation -ICC_Profile:ProfileDescription -Model original.jpg prints all four.
- **AC3 (migrated legacy original loses its GPS tags)**
  - Automated: step 9.
  - Operator:
    - exiftool -if '$GPSLatitude' -p '$Directory/$FileName' -r /workspaces/legacy-data/images/userpics/user_pics | head -1
    - Map the path to media_item.storage_key (originals/ plus the normalised legacy path).
    - After a normal seed run, download and inspect as for AC2: no GPS.
- **AC4 (P3 colours match, variant has no metadata)**
  - Automated: step 4.
  - Operator: upload a Display P3 phone photo and compare the original with the medium variant in a colour-managed viewer (Safari, macOS Preview, or Firefox with full colour management). exiftool -a -G1 on the variant lists no EXIF, ICC or XMP groups.
- **AC5 (200000 x 200000 file rejected, memory bounded)**
  - Automated: steps 3 and 9.
  - Operator: curl -F 'file=@bomb.png;type=image/png' with a bearer token against /api/uploads returns 400, and the API RSS stays flat.
- **AC6 (undecodable picture loads from a public copy)**
  - Automated: steps 10 and 11.
  - Operator: run the seed with --missing-out report.json, take an entry under copies, open its picture in the app; the image loads from /variants/copy/.
- **DoD:** the e2e path is media-privacy.write.mjs via pnpm run e2e:write. .NET suites run in CI once TASK-2.28 lands; until then run dotnet test app/api/Cichlids.sln locally.

### Risks

- **RustFS prefix policies:** if RustFS ignores prefixed resources in bucket policies, step 1 fails first. The fallback would be a separate bucket for originals, which needs a new decision.
- **Production serving store:** Cloudflare R2 has no bucket policies. Production must copy only variants, forum attachments and copies to R2, while originals stay in the private Hetzner master (ADR-0009 amendment, TASK-2.4).
- **Versioned master buckets:** the media master is versioned and Object Lock protected, and the backup bucket uses Compliance lock. Overwriting unsanitized originals there would keep GPS data in noncurrent versions. No seed may run against Hetzner buckets before this task lands.
- **In-house parsing edge cases:** malformed EXIF and vendor quirks are mitigated by dropping the whole EXIF block on parse failure, by zeroing dropped value bytes, and by an independent parser as the test oracle.
- **Ultra HDR and gain-map JPEGs:** the gain map is lost in the original because data after EOI is truncated. Accepted, since that trailing data carries its own EXIF.
- **Global loader block:** the libvips loader block applies to the whole process, so any future NetVips user is subject to the same allowlist.
- **Dev re-seed cost:** bumping both pipeline versions makes the next dev seed re-upload all originals and regenerate all variants (about the full seed time, 94 minutes, 283 GB).
- **Video originals:** about 30 legacy videos are stored privately and not delivered; their location atoms stay untouched. Stripping video metadata belongs to the ADR-0012 pipeline.
- **Breaking API change:** ImageUrlsDto.Original is removed. The only client use was the draft fallback, and the app is not released.
<!-- SECTION:PLAN:END -->
