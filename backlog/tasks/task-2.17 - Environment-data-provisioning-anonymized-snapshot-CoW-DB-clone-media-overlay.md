---
id: TASK-2.17
title: >-
  Environment data provisioning: anonymized snapshot, CoW DB clone, media
  overlay
status: In Progress
assignee: []
created_date: '2026-06-08 13:14'
updated_date: '2026-10-15 06:42'
labels:
  - infra
milestone: m-0
dependencies:
  - TASK-2.16
parent_task_id: TASK-2
ordinal: 19000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Implement ADR-0018: realistic, PII-free data for staging and preview without copying the
terabyte-scale media corpus per environment.

### Scope
- Anonymization pipeline: restore prod backup in isolation, mask/anonymize PII, publish a
  versioned 'golden' snapshot (anonymized DB image + representative media subset).
- Staging refreshed from the golden snapshot.
- Preview: ephemeral DB cloned per PR via copy-on-write (CSI VolumeSnapshots / restore from base
  backup); media served via overlay (shared read-only base + ephemeral per-preview write store),
  no full-corpus copy.
- Optional minimal synthetic seed for fast smoke previews (not the default).

### Realises
- ADR-0018 (environment data), extending ADR-0013; relies on the cichlids-backup project from ADR-0017.

### Note
- No implementation in this task's creation — planning/coding happen when picked up.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the anonymized golden snapshot, When a preview environment is created, Then it gets an ephemeral copy-on-write database clone and serves media via a shared read-only base plus an ephemeral per-preview write overlay, without copying the full media corpus
- [ ] #2 Given staging, When refreshed, Then it is populated from the anonymized golden snapshot and contains no production personal data
- [ ] #3 Given the anonymization pipeline, When it runs, Then production personal data is removed or masked before the snapshot is published
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Decided masking (project owner, 2026-10-15)
- Source: the migrated legacy data now, a production backup later; the anonymizer runs out of
  band at the operator (no CI or agent access to real personal data); agents build and test it
  with synthetic fixtures only; the result is published to a private bucket as a versioned
  golden snapshot.
- No Keycloak data from production; staging and previews use test accounts only.
- Usernames and display names: stable pseudonyms `member-<n>`; city, avatar URL, last login and
  suspension data removed, country kept; `profile_identity` removed; guest names `guest-<n>`.
- Every free text (comments, forum posts, titles, descriptions, tank texts, collection titles,
  delete reasons) replaced by generated text of similar length; counts, structure and timestamps
  kept.
- `archive.legacy_comment`, outbox events and webhook subscriptions emptied; species unchanged.
- Media: a curated subset of about 2,000 pictures, public variants only, never originals,
  original file names replaced.
- Staging and previews are reachable only from the tailnet (separate task); certificates through
  HTTP-01 with only the ACME challenge path public.

### Implementation
- `tools/golden-snapshot/` (anonymizer with a verification step that fails when any pattern of
  real data survives: e-mail addresses, URLs to personal sites, original usernames from a
  denylist given at run time), tests on synthetic fixtures, a restore path for staging and the
  preview clone (copy-on-write through a CloudNativePG restore from the snapshot backup or a
  volume snapshot; choose the simplest robust variant on single-node k3s).
- Media overlay: read-only base prefix of the subset plus a per-preview write prefix.
<!-- SECTION:PLAN:END -->
