---
id: TASK-2.9
title: CloudNativePG with object-store PITR backup
status: In Review
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-10-14 06:15'
labels:
  - infra
  - postgres
  - backup
  - gitops
milestone: m-0
dependencies:
  - TASK-2.7
references:
  - apps/postgres/
  - tools/check-public-exposure.mjs
  - .github/workflows/exposure-watch.yml
  - apps/postgres/README.md
  - tools/verify-backup-writer-policy.sh
parent_task_id: TASK-2
priority: high
ordinal: 11000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Run PostgreSQL in-cluster via the **CloudNativePG** operator and make backup binding from day
one (ADR-0013).

### Scope
- Define the operator and a `Cluster` CR, with **Barman Cloud** object-store backups writing
  base backups and continuously archived WAL to the backup bucket (RPO≤5min).
- Tiered retention: a 14-day continuous PITR window plus monthly archival snapshots retained
  12 months.
- Staging and preview get their own ephemeral databases seeded with synthetic data (never
  production PII).
- The Cluster, its PVCs and namespace carry `Prune=false` and finalizers (ADR-0014 Layer 1).

### Realises
- ADR-0010 (CloudNativePG), ADR-0013 (continuous archiving, PITR, retention, RPO).

### Verifying artifact
- `apps/postgres` rendering + a policy check that backup, retention and `Prune=false` are present.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given apps/postgres, When kustomize build runs, Then it renders and passes schema validation
- [ ] #2 Given the Cluster CR, When inspected, Then it configures Barman Cloud object-store backup with continuous WAL archiving, a 14-day PITR window and 12 monthly snapshots
- [ ] #3 Given the stateful resources, When checked, Then the Cluster, PVCs and namespace set Prune=false and finalizers
- [ ] #4 Given staging/preview, When provisioned, Then their databases use synthetic seed data and never production PII
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach (test-first)
- Red first: extend the manifest checks with a policy check for `apps/postgres` (backup via the
  Barman Cloud plugin, WAL archiving, schedules, no plugin-side deletion, Prune=false and
  finalizers on Cluster, PVCs and namespace) and a guardrail test for the backup tier bucket
  policy; both fail before the code exists.

### Operator and plugin (addons)
- CloudNativePG operator and the Barman Cloud plugin, pinned releases, deployed once in the
  addons project (the plugin uses cert-manager, which exists).

### Production database (`apps/postgres`, overlay prod)
- `Cluster` with one instance, storage on the node, `ObjectStore` pointing at the database backup
  bucket of the backup project; continuous WAL archiving (archive timeout at most 5 minutes for
  RPO <= 5 min), a daily base backup and a monthly base backup through `ScheduledBackup`.
- The plugin never deletes: no retention policy in CloudNativePG. Old objects expire through the
  bucket: the Compliance retention of the database backup bucket covers the longest retention
  (monthly backups for 12 months, so at least 366 days plus margin) and an expiration lifecycle
  rule removes objects after it; the PITR window is therefore at least 14 days.
- Credentials: a dedicated object storage key from the primary project `cichlids`, stored as a
  SOPS-encrypted Secret (created by the operator with the age key), never a key of the backup
  project.

### Backup tier (applied by the operator)
- `aws_s3_bucket_policy` on the database backup bucket: the cluster key (principal
  `user/p<project id>:<access key>` of the primary project, from variables) may only put, get and
  list objects and handle multipart uploads; no delete, no bucket configuration. Retention days of
  the database backup bucket raised as above; expiration lifecycle rule if the provider supports it.
- An operator script verifies the policy with the cluster key: put and get succeed, delete and
  any bucket configuration change are refused.

### Staging and previews
- Their own databases from the same base without backup to the backup bucket, never restored
  from production; empty schema plus synthetic seed data only (link to TASK-2.17 for the seeding
  mechanism, never production PII).

### Docs and ADRs
- ADR-0017 and ADR-0013 state that the cluster writes backups with a key of the primary project
  that a bucket policy limits to writing and reading, so no key of the backup project leaves the
  operator; restores use the same path.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Decided by the project owner
- Backups are stored without client-side encryption: no mature tool for CloudNativePG encrypts
  before upload (Barman Cloud supports only AWS SSE-S3 and KMS, Hetzner only SSE-C). ADR-0013
  states this with its reason; client-side encryption follows once such a tool exists.
- In return a double safeguard against accidental public exposure of the backup buckets and the
  media originals: static checks before every change (backup and platform guardrails, a plan
  policy, the API bucket policy test) and a keyless external probe on a schedule
  (`exposure-watch` workflow with canary objects) that fails loudly when a bucket or prefix is
  readable or listable by anyone.

### Storage
- Production PostgreSQL lives on the protected database volume of the platform tier: the app tier
  mounts it (ext4, never formatted, `nofail`) at `/var/lib/cichlids-db`; a static local
  PersistentVolume with Retain, 8Gi, fits the existing 10 GB volume (the migrated database
  measures about 1.8 GB); grow the volume when usage passes 70 percent.

### Implemented
- CloudNativePG v1.30.1 and the Barman Cloud plugin v0.15.1 in the addons project; PostgreSQL
  18.6. Production `Cluster` with `ObjectStore` on `cichlids-db-backup`, archive timeout 5 minutes,
  daily and monthly `ScheduledBackup`, no plugin retention (the plugin never deletes).
- Backup tier: bucket policy for the writer key of the primary project with exactly
  `s3:ListBucket`, `s3:GetObject`, `s3:PutObject`, `s3:AbortMultipartUpload`; Compliance retention
  400 days; lifecycle: delete marker after 400 days, unlocked noncurrent versions expire one day
  later. Hetzner supports only SSE-C, so no server-side encryption is configured.
- Staging and previews: own clusters on `local-path`, no backup to the backup bucket, never
  restored from production (synthetic seed data: TASK-2.17).
- Exposure safeguards: backup and platform guardrails (33 and 30 self-test cases), plan policy
  against public bucket changes, API test of the media policy allowlist, keyless probe
  `tools/check-public-exposure.mjs` (self-test 51 cases) in the monthly workflow
  `exposure-watch` (first day, 05:17 UTC); a live anonymous run answered 403 on all 12 checks.
- ADR-0013, ADR-0014 and ADR-0017 describe the writer key, the lock-based retention and the
  exposure safeguards.

### Operator checks
- Writer key, backup tier apply, `tools/verify-backup-writer-policy.sh`, canary objects, first
  `exposure-watch` run, the writer secret in `cichlids-prod`, then a base backup and WAL archive
  in the bucket and a test restore (TASK-2.10). AC #4 depends on TASK-2.17.
<!-- SECTION:NOTES:END -->
