---
id: TASK-2.17
title: >-
  Environment data provisioning: anonymized snapshot, CoW DB clone, media
  overlay
status: To Do
assignee: []
created_date: '2026-06-08 13:14'
updated_date: '2026-06-08 13:14'
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
