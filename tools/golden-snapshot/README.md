# Golden snapshots

The operator creates a versioned, pseudonymized application database and a shared
subset of public photo variants. Agents and CI use generated fixtures exclusively.
Keycloak databases and production backup credentials are outside this tool.

Python coordinates PostgreSQL client tools, HMAC generation and S3 transfers.
The standard library provides deterministic text and pseudonyms; psycopg supports
parameterized SQL and bounded batches. SQL in `mask.sql` handles bulk removal.
The same image supplies matching PostgreSQL 18 clients for creation and restores.

## Masking contract

The result is pseudonymized. Row IDs, legacy IDs, story IDs and creation times stay
stable, and public production routes address these IDs, so a snapshot row can be
matched to a public profile, tank or story. Free text, names, contact data and the
private scopes listed below are removed or replaced. Handle snapshot access as access
to pseudonymized personal data. IDs are not renumbered.

| Data | Transformation |
| --- | --- |
| Profile username and display name | `member-<n>`, with a 96-bit HMAC-SHA256 integer per profile ID and runtime key. Independent of row order and membership of the source population. Collisions abort. |
| City, external avatar URL, last login, suspension, time of the last request and start of an absence | NULL. Country, profile kind, IDs and creation time are preserved. |
| Media links with context `profile` (roles `avatar`, `header`) | Removed to exclude profile pictures. |
| `profile_identity`, `web_session` | Empty, including subjects and encrypted tokens. Test logins are provisioned separately. |
| Comment and discussion guest names | `guest-<n>`, HMAC per table row ID. |
| All comment bodies and delete reasons, discussion bodies and thread titles, story titles/descriptions/delete reasons, collection titles, care entry notes, media link captions | Generated text with exactly the original character count; NULL and empty strings preserved. HMAC of table, column and row ID chooses words. |
| Tank title, description, gravel, plants, decoration, light, light duration, filtration, technic, all water text fields, food, notes, delete reason | Same generated text rule, including water values stored as strings. |
| `slug_alias.value` | `story-<story-id>-alias-<alias-id>`, retaining aliases and canonical flags. |
| `archive.legacy_comment`, `outbox_event`, `webhook_subscription` | Empty. |
| `web_push_subscription`, `web_push_reminder_sent`, `video_upload`, `video_job` | Empty. Endpoints, keys, upload names, storage keys and job state are removed. Videos are excluded from the media subset. |
| Species, species names and reference links | Preserved. |
| Ratings, approved follows of public tanks and of members, inhabitants, collections, story species subjects and story covers | Preserved, including IDs, numeric values, ordering and timestamps, except the private scopes below. |
| Care entries (kind, times, amounts, evidence state and finding codes), measurement values, plan rules and rule history, water change sessions and steps, streak freezes, visibility consents, profile streak setting | Preserved for tanks with a public care stream, including IDs, values, kinds and times. Only notes are replaced. |
| Provisional profiles | Profiles of kind `provisional` belong to unregistered installations and their content is private by design. They are removed with their tanks, care data, media items and links, stories, comments, ratings, follows, collections and forum posts, and their media are not eligible for the photo subset. `device_credential` and `device_challenge` are emptied, including their key and challenge bytes. |
| Private scopes | Removed: `vacation`, `care_reminder_snooze`, `care_todo`, `comment_vote`, `tank_selected_member`, `released_handle` and `used_checksum` (empty; checksums of private photos tie photos to members); pending follows; follows of tanks whose profile visibility is not public, because the follow list of a restricted tank reveals who is allowed to see it; care entries marked only-me, with their measurement values, streak freezes, links and the water change session behind them; care entries, plan rules and rule history, water change sessions with steps and links, streak freezes, decommissionings and visibility consents of tanks whose stream visibility is not public (the levels `private`, `selected` and `followers` all count as not public); inhabitants, story subjects that point to such tanks or their inhabitants, tank media links, tank comments and tank ratings of tanks whose profile visibility is not public; collection entries of non-public collections. |
| Media placement state | Every retained media item is placed public (its variants are public objects of the shared golden prefix), has no placement due, zero attempts, no next attempt time and visibility generation 0. A restored environment moves no files and issues its own signed links. |
| Quick share story reference | `media_item.quick_share_story_id` is preserved. All stories are preserved, so the reference stays valid. |
| Media capture metadata | Capture time, capture offset and capture check time are NULL, the receive time equals the creation time and the in-app flag is false, so photo series cannot be correlated with a member. |
| Tank decommissionings | Preserved when the start photo and the end photo, if any, remain in the media subset. A decommissioning with a photo outside the subset is removed, because its photo references cannot be emptied. |
| Media | One photo per selected published story (the first media link of role `item` by position), up to 2,000 stories by deterministic HMAC sampling, or explicit story IDs. Only generated `thumb`, `small`, `medium`, `large` JPEG variants qualify. Originals, videos, public-copy fallbacks and forum attachments are excluded. |
| Original file names, media keys and checksums | File name `media-<id>.jpg`, variant key `golden/<version>/media/variants/<label>/media-<id>.jpg`. The media item's key resolves to its thumb; the original checksum is NULL. Variant objects receive only content type and a generated checksum metadata field. |
| Media relationships | Media links to excluded media are removed, whatever their context (tank, inhabitant, care entry, water change session including evidence photos, post, discussion post). Links of retained media in other contexts are preserved with a generated caption. A story cover whose media link is removed becomes NULL; the verification checks that no cover points to a removed link. Posts, comments and their interaction counts are preserved even when their photos are outside the subset. |

`schema.json` specifies every supported column and PostgreSQL type. A schema
mismatch aborts before copying. Extra schemas, views, materialized views,
large objects, foreign servers, custom types, extensions, event triggers,
rules, policies and logical replication objects also abort. Functions and triggers
are allowed only as listed in `routines.json`: each entry names the function (with
argument types) or the trigger (with its table) and holds the SHA-256 of its definition
text (`pg_get_functiondef`, `pg_get_triggerdef`). Any other function or trigger, a changed
definition and a missing entry abort. The listed ones are the media visibility functions and
triggers of the EF migrations, which bump the visibility generation of media items and mark
their placement as due, and the provisional content guard, which refuses writes of public
content for provisional profiles. `fixture.sql` makes its provisional profile provisional
after its content exists, since the guard refuses that content. `fixture-schema.sql` is generated from the EF
model (`dotnet ef dbcontext script`) and includes its foreign keys, checks and indexes, followed by
the functions and triggers of the migrations, the migration history table and the latest migration
ID, which `schema.json` and the manifest follow. The triggers fire during masking, so masking
resets the placement state as its last step. `pg_restore` creates triggers after loading the data,
so a restore keeps the stored state and a restored environment has no placement due.
Review schema and masking coverage together when migrations change.

`COLUMN_HANDLING` in `snapshot.py` states for every column of `schema.json` how it is
handled: generated text, pseudonym, NULL, rewritten alias, rewritten media key or fixed value,
empty table or kept. Kept columns include fixed vocabularies (kinds, states, units, roles,
finding codes), system values (reason codes and parameters, migration IDs), numbers, times and
the public species catalog; the verification scans text columns like all others. A unit test
compares this table with `schema.json`, so a new column of any type fails the test until it has a
rule.

| Handling | Columns |
| --- | --- |
| Generated text of the same length | Comment body, delete reason; discussion post body; discussion thread title; post title, description, delete reason; collection title; tank title, description, equipment, water and food fields, notes, delete reason; care entry note; media link caption |
| Pseudonym | Profile username and display name; comment and discussion post guest name |
| NULL | Profile city, external avatar URL, last login, time of the last handle change, time of the last request, start of an absence and suspension; media item capture time, offset, check time and next placement attempt |
| Rewritten | Slug alias value; media item storage key, original file name, content type, checksum, receive time, in-app flag and placement state; media variant storage key |
| Empty table | `archive.legacy_comment`, `profile_identity`, `web_session`, `outbox_event`, `webhook_subscription`, `web_push_subscription`, `web_push_reminder_sent`, `video_upload`, `video_job`, `vacation`, `care_reminder_snooze`, `care_todo`, `comment_vote`, `released_handle` (handles that members gave up, reserved for them) |
| Kept | IDs, numbers, flags and times; kinds, states, categories, regions, visibility levels, units, parameters, origins, roles, context types, scopes and step names; evidence state and finding codes; plan reason code and parameters; profile country code, kind, interests and badges; species catalog; migration history |

The source session is read-only and exports a repeatable-read snapshot. `pg_dump`
uses that snapshot and streams directly into a fresh target through `pg_restore`.
No raw dump is written to disk. Source and target identity are compared through
PostgreSQL system identifier and database name, including alternate host names.
The operator needs permission to execute `pg_control_system()` on both servers.
The target must be empty and isolated from application connections.

All masking and verification execute in one transaction on the copied target.
Verification scans text, arrays and JSON for email patterns, HTTP(S) URLs and
case-insensitive occurrences of original usernames, display names, guest names
and original file names. Defaults, constraint definitions and index definitions are
checked for the same patterns. The denylist exists only in process memory. Output
contains counts only; database and S3 errors are suppressed. The URL pattern check
excludes `species`, `species_common_name` and `species_link`: the species catalog
contains public reference material. Email and denylist checks apply to every table.
Short identifiers can cause conservative false positives.

A failed transaction leaves the isolated target with the raw copied contents.
Destroy that target after either outcome. Keep its volumes, PostgreSQL statement
logs, audit logs, host crash dumps and process inspection restricted to the
operator. Disable statement/parameter logging and core dumps for the isolation
process. A failed verification emits no snapshot artifacts. Failed uploads can
leave a partial private version prefix; use a fresh version for the next attempt.
An existing prefix is rejected. Concurrent publishers require operator
serialization. The manifest is uploaded last as the completion marker.

## Operator: create and publish

Run outside CI on an isolated workstation. Use a private bucket in the primary
project, with public access disabled. The golden publisher may list and put under
`golden/`; its media-source credentials may get selected public variants only.
Neither staging nor previews receive those credentials. Production backup access
belongs exclusively to the operator's isolated restore process (ADR-0013).

1. Restore the application database into an isolated source through the separate
   operator backup procedure. Create an empty isolated target. Provision protected
   environment variables `GOLDEN_SOURCE_DSN`, `GOLDEN_TARGET_DSN`,
   `GOLDEN_HMAC_KEY` (at least 32 random bytes encoded as hex),
   `GOLDEN_S3_ENDPOINT`, `AWS_DEFAULT_REGION`, `AWS_ACCESS_KEY_ID`,
   `AWS_SECRET_ACCESS_KEY`, `GOLDEN_BUCKET`, `GOLDEN_MEDIA_SOURCE_BUCKET`,
   `GOLDEN_VERSION` and `GOLDEN_OUTPUT`. DSNs and keys must come from an operator
   secret store, never command arguments, shell history or repository files.
   Keep the HMAC key to reproduce pseudonyms; rotate it to change identities.
2. Install Python 3.12+, PostgreSQL 18 client tools and dependencies:

   ```sh
   umask 077
   ulimit -c 0
   python3 -m venv "$GOLDEN_VENV"
   "$GOLDEN_VENV/bin/pip" install -r tools/golden-snapshot/requirements.txt
   ```

3. Review photo selection and content outside agent/CI access. The default samples
   about 2,000 eligible pictures. For an explicit selection, supply a JSON array of
   numeric story IDs using `--story-ids "$GOLDEN_STORY_IDS"`. Publish:

   ```sh
   "$GOLDEN_VENV/bin/python" tools/golden-snapshot/snapshot.py \
     --output "$GOLDEN_OUTPUT" --version "$GOLDEN_VERSION" \
     --publish-bucket "$GOLDEN_BUCKET" \
     --media-source-bucket "$GOLDEN_MEDIA_SOURCE_BUCKET"
   ```

   The runtime media map is held in memory. Objects are uploaded as
   `golden/<version>/media/variants/<label>/media-<id>.jpg`. Public pipeline JPEGs
   have embedded metadata stripped; the operator reviews the pixels for personal
   information. S3 metadata is replaced during upload. Original object keys never
   enter the manifest. `snapshot.dump` uses PostgreSQL custom format. The manifest
   includes the schema migration ID, table row counts, selected story IDs, variant
   keys, object/dump SHA256 checksums and format/version fields. Running without
   publishing produces an inspection dump with `media_complete: false`; restores
   reject this incomplete artifact.
4. Check `VERIFICATION: leaks=0`, `SNAPSHOT` counts and `PUBLISHED: snapshots=1`.
   An independent verification of the isolated target uses:

   ```sh
   "$GOLDEN_VENV/bin/python" tools/golden-snapshot/snapshot.py --verify-only --version "$GOLDEN_VERSION"
   ```

5. Destroy the isolated raw source and target through the operator cleanup
   procedure, revoke temporary credentials and remove the local output after
   checking the private published objects. Retain the version and key securely.

## Staging and preview provisioning

`apps/postgres/golden` adds a PostSync restore Job to the staging and preview
PostgreSQL Applications. CNPG initializes the environment's own empty `cichlids`
database. The Job downloads the custom dump and manifest through a read-only
`golden-reader` Secret, checks SHA256, restores in one transaction, and checks
migration ID and table row counts. Media HEAD checks validate checksum metadata;
media bodies stay in the shared bucket. The Job uses the namespace's generated
`postgres-app` connection URI. It has no Kubernetes API token or production
backup access.

A session advisory lock serializes jobs. A database comment records version and
dump hash. A matching marker makes retries a no-op, preserving environment writes.
A populated database with a different version fails closed. A failed restore
cannot create the success marker. Application writers must stay stopped until
restore and count verification succeed. PostgreSQL Applications and API
Applications reconcile independently; the operator gates API activation on the
successful Job. Preview API integration needs this readiness dependency.

The `local-path` provisioner supplies ordinary local PVCs without CSI snapshots.
Each preview logically restores the golden dump into its disposable 1Gi PVC.
This physically duplicates the database and takes time proportional to dump size.
It requires no snapshot controller or physical backup credentials. Media is shared
per version. This is the logical-restore fallback to ADR-0018; copy-on-write speed
and disk savings are unavailable. Measure the restored database and dump sizes:
previews have a 1Gi database quota, the restore Job allows 2Gi ephemeral storage,
and staging has 20Gi. A larger golden database requires an explicit quota/storage
change. Production Barman backup and PITR configuration are independent.

Operator activation order:

1. Publish a complete golden version. Build and publish the restore image from
   this directory to the configured primary-project registry:

   ```sh
   docker build -t "$GOLDEN_RESTORE_IMAGE" tools/golden-snapshot
   docker push "$GOLDEN_RESTORE_IMAGE"
   ```

   Set `apps/postgres/golden/restore-job.yaml` to its immutable image digest.
   The checked-in `:1` name is the operator image contract.
2. Provision `golden-reader` in `cichlids-staging` and each admitted `pr-<number>`
   namespace before syncing its PostgreSQL Application. Secret keys are
   `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY`, using a restricted identity
   with only `s3:GetObject` on the selected `golden/<version>/*` prefix. The Secret
   can be provisioned by the operator or an external secret controller. Encrypt
   repository-managed credentials with the configured operator SOPS recipient.
   The reader needs no list, put, delete, originals or production backup access.
3. Set the version, private bucket and endpoint in the golden ConfigMap. The
   `unpublished` version fails closed until the operator supplies an artifact.
   Sync PostgreSQL. The preview ApplicationSet includes the Job from the PR's
   immutable revision, applies the actual PR namespace and deletes the disposable
   Cluster/PVC/Job with that preview. Provision reader credentials for every new
   namespace. Each newly admitted PR gets a fresh database; repeated syncs preserve
   its writes. A rejected/expired PR follows the existing ApplicationSet teardown.
4. For an existing staging database, stop API/background writers and record the
   desired version. Use an operator connection to that staging application
   database, with the same reader environment, and run:

   ```sh
   "$GOLDEN_VENV/bin/python" tools/golden-snapshot/restore.py --refresh
   ```

   Set `GOLDEN_ENVIRONMENT=staging`; `GOLDEN_TARGET_DSN` identifies staging only.
   This explicit operation uses `pg_restore --clean --if-exists` in one transaction
   inside the application database. Keycloak has its own database. Check
   `RESTORE: tables=46 verified=1`, restart application writers, then sync the
   ordinary Job to confirm the matching marker. A changed golden version never
   refreshes an existing database automatically.

## Media overlay

The API uses one shared non-production bucket. The publisher stores the curated
subset under `golden/<version>/media/`; each environment writes its own prefix.
The snapshot database stores the exact published keys, such as
`golden/<version>/media/variants/thumb/media-1.jpg`. Both `media_item.storage_key`
and exported `media_variant.storage_key` values use this prefix. Excluded media,
variants and their relationships are removed by masking. The manifest's `media`
and `media_keys` entries contain these same full keys; publication and restore
verification use them directly. Restores require full versioned media keys in the
manifest and a complete published subset.

| Setting | Meaning |
| --- | --- |
| `ObjectStorage:Bucket` | Bucket containing the golden subset and environment objects |
| `ObjectStorage:WritePrefix` | `environments/staging/` or `environments/pr-<number>/`; empty by default |
| `ObjectStorage:PublicBaseUrl` | Tailnet-only media gateway base URL for this bucket |

Golden reads use the stored key directly. URL generation encodes that key and
performs no storage requests. Environment keys use `WritePrefix` for uploads,
reads, URLs, deletes and listings. Listings return environment-relative keys and
exclude golden objects. A missing environment object returns the ordinary
missing-object result.

Writes and deletes under `golden/` are refused for every configuration. A golden
write prefix is invalid. Discard and draft cleanup may remove database references
to shared media; the shared objects are protected by the object store guard.

Configure `ObjectStorage:Bucket` to match `GOLDEN_BUCKET`. The snapshot version is
selected by the publisher and database restore Job's `GOLDEN_VERSION`. Production
uses the default empty `WritePrefix`.

The operator provisions bucket permissions: the API identity may read its own
prefix and the selected golden media prefix, and may put/delete only under its
own environment prefix. Bucket listing must be scoped to the environment prefix.
The publisher has separate credentials. The media gateway must serve both media
prefixes through the tailnet and preserve private bucket access. Bucket creation
and anonymous public-read policy setup belong to operator provisioning; the API
does not invoke them. Each preview teardown removes only its environment prefix.
Live permissions and gateway routing require deployment verification.

## Verification

```sh
python -m pip install -r tools/golden-snapshot/requirements.txt
python -m unittest discover -s tools/golden-snapshot -v
./tools/check-cluster-manifests.sh
actionlint
python3 tools/check-secret-manifests.py
node tools/check-story-test-binding.mjs
```

Database tests require `GOLDEN_TEST_DSN` pointing to a newly created synthetic
PostgreSQL service and matching `pg_dump`/`pg_restore` clients. Its database name
must start with `synthetic_`. The fixture creates separate randomly named source,
target and clone databases and drops them in teardown. It tests the real custom
dump restore, every masking rule, unchanged source contents, stable pseudonyms,
masking rollback, denylist checks, populated-target refusal, staging refresh and
idempotent preview initialization. `GOLDEN_MIGRATED_DSN` points to a synthetic database (name starting with `synthetic_`) migrated by `dotnet ef database update`; `test_migrated_schema.py` then checks `schema.json`, the table list and the pinned migration ID of `fixture-schema.sql` against it. A subprocess with a planted email must print
`VERIFICATION: leaks=1` and exit 1. CI's `golden-snapshot` job runs this path and the migrated-schema check using
an isolated PostgreSQL service. CI has no source-data or publisher credentials.

Live S3 policy enforcement, CNPG Job reconciliation, application readiness,
operator-selected media content and restored capacity require operator checks.
