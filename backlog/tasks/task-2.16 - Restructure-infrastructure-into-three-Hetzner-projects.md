---
id: TASK-2.16
title: Restructure infrastructure into three Hetzner projects
status: In Progress
assignee:
  - Claude
created_date: '2026-06-08 13:14'
updated_date: '2026-06-08 14:55'
labels:
  - infra
milestone: m-0
dependencies:
  - TASK-2.13
references:
  - infra/README.md
parent_task_id: TASK-2
ordinal: 18000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Implement the capability-based blast-radius boundary from ADR-0017: split the infrastructure
across three Hetzner projects so the routine CI token cannot reach the irreplaceable data.

### Scope
- Three projects: `cichlids` (all live/rebuildable infra for every env), `cichlids-backup`
  (immutable DB + media-master backups, Object Lock Compliance), `cichlids-tfstate` (state
  bucket only).
- Separate hcloud providers/tokens and state keys per project; CI holds only the `cichlids`
  R&W token + S3 keys, and no Cloud token for backup/tfstate.
- Rework the current single-project foundation (node/volume/buckets/DNS in one project) and the
  bootstrap runbook accordingly (three projects, split credential flow).

### Realises
- ADR-0017 (project separation), refining ADR-0014 Layer 2; consistent with ADR-0016 (state).

### Note
- Reworks the single-project foundation/runbook delivered earlier; the foundation apply is
  gated on this. No implementation in this task's creation — planning/coding happen when picked up.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the OpenTofu configuration, When applied, Then live infrastructure is in the cichlids project while backups and state are in separate cichlids-backup and cichlids-tfstate projects
- [ ] #2 Given CI, When it runs OpenTofu, Then it holds only the cichlids project Read & Write token plus object-storage keys, and no Cloud token for the backup or state projects
- [ ] #3 Given the bootstrap runbook, When the operator follows it, Then the three projects and their credentials are created with the correct out-of-band vs in-CI split
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Goal

Realise ADR-0017: split the single-project foundation into three Hetzner projects along a
credential/state boundary, so the routine CI token (primary project only) can never reach the
irreplaceable backups or the OpenTofu state. Refines ADR-0014 Layer 2; consistent with ADR-0016.

### Target layout (three projects, separate state keys)

- **`infra/foundation/` -> project `cichlids` (primary)**: k3s node + SSH key, DB block volume,
  media-master bucket (originals, versioning + Object Lock *Governance*) and serving, Hetzner
  DNS zone. Providers: `hcloud` (primary Read & Write token), `aws` (primary-project S3 keys),
  `hetznerdns`. Backend state key `foundation/terraform.tfstate`.
- **`infra/backup/` -> project `cichlids-backup`**: the DB PITR/WAL backup bucket and the
  media-master-backup bucket, both versioning + Object Lock **Compliance**, `prevent_destroy`.
  Provider: **`aws` only**, pointed at the backup project's object storage with the
  **backup-project S3 keys**. **No `hcloud` provider, no cloud token.** Backend state key
  `backup/terraform.tfstate` in the same dedicated `cichlids-tfstate` bucket. Applied out-of-band
  by the operator; never CI.
- **project `cichlids-tfstate`**: the state bucket only (versioned, no Object Lock), still created
  by hand out-of-band (ADR-0016, unchanged).

### What moves / changes

- Move `aws_s3_bucket "backup"` (DB PITR) out of `infra/foundation/buckets.tf` into
  `infra/backup/`. Keep `media_master` in foundation.
- New `infra/backup/` module: `versions.tf` (aws provider only), `providers.tf` (aws -> backup
  S3 keys + endpoint), `backend.tf` (state key `backup/...`, same bucket, native encryption),
  `variables.tf` (backup S3 keys, bucket names, retention, enable_object_lock), `buckets.tf`,
  `outputs.tf`, `main.tf` (labels: `project = "cichlids-backup"`), `terraform.tfvars.example`.
- `infra/foundation/`: drop the backup bucket + its variables/outputs; keep node, volume,
  media-master, DNS; `providers.tf` stays primary-only.
- Credential model documented: primary hcloud token (CI + operator), primary S3 keys (CI +
  operator), backup S3 keys (operator / running-system only -- **never a CI cloud token**),
  state S3 keys (CI backend + operator), account-level DNS token (operator; later CI for
  cert-manager DNS-01).

### Red-Green (test-first; offline .tf parsers, no cloud creds -- same pattern as the existing checks)

1. **New `tools/check-project-separation.py` (+ `--self-test`)** -- asserts the project/credential
   boundary:
   - `infra/backup/` exists and declares **no `hcloud` provider block and no hcloud token
     variable** (the backup project holds no cloud token).
   - the DB-PITR bucket and the media-master-backup bucket live in `infra/backup/`, each with
     `prevent_destroy`, versioning Enabled, and Object Lock **Compliance**.
   - `infra/foundation/` no longer contains `aws_s3_bucket "backup"`; still contains
     `media_master`; still declares the `hcloud` provider (the single cloud-token tier).
   Write it first -> **red** against the current single-project layout.
2. **Extend `tools/check-remote-state-backend.py`**: add `backup` to `TIERS` and assert **distinct
   state keys per tier** (`foundation/...` vs `backup/...`) in the same dedicated bucket; update
   its self-test. Red against the current state (no backup tier, no key-distinctness check).
3. **Split the guardrails check**: trim `infra/foundation/guardrails_test.py` to node / volume /
   media-master / dns, and add `infra/backup/guardrails_test.py` for the backup buckets
   (prevent_destroy + versioning + Compliance). Red until the backup module exists.
4. **Make green**: create `infra/backup/`, move the bucket, update foundation, until all three
   checks plus `tofu fmt -check -recursive infra` and `tofu validate` for both modules pass.

### CI wiring (DoD #3: all CI green)

- `infra-validate` job: add `tofu -chdir=infra/backup init -backend=false && tofu -chdir=infra/backup validate`;
  run `infra/backup/guardrails_test.py`; run `tools/check-project-separation.py --self-test` then
  `tools/check-project-separation.py`. `tofu fmt -check -recursive infra` already covers both
  modules; `check-remote-state-backend.py` already runs here and now also covers the backup tier.

### Runbook (infra/README.md) rework -- numbered operator steps, each with expected result

- "Three projects" section: create `cichlids`, `cichlids-backup`, `cichlids-tfstate`.
- Per project: which credentials, and where each goes (CI secret vs out-of-band only):
  - `cichlids`: one Read & Write cloud token (-> CI secret + operator) and S3 keys (-> CI +
    operator).
  - `cichlids-backup`: S3 keys only, **out-of-band only, never a CI secret**; **no cloud token
    anywhere in automation**.
  - `cichlids-tfstate`: S3 keys only (-> CI backend + operator); state bucket created by hand;
    **no cloud token**.
- Apply order with expected result per step: state bucket (by hand) -> `infra/backup` apply
  (operator, backup S3 keys) -> `infra/foundation` apply (operator). Object Lock at go-live;
  break-glass procedure unchanged.
- Update the "GitHub Actions secrets" list to explicitly exclude any backup/state cloud token --
  only the primary token + S3 keys + state passphrase + DNS token + R2.

### Story<->test binding & DoD

- Infra/ops task: no Maestro E2E. DoD is already tailored (AC specified / red-green / all CI
  green); the red-green is the three static checks above. Add the task-id back-tag comment to
  `infra/README.md`; `references` = that single file.

### Confirmed decisions

1. **media-master-backup bucket**: declared now in `infra/backup/` (empty until the copy workload
   lands), so the three-project structure and its guardrails are complete from the start.
2. **`enable_object_lock` toggle on the backup module**: keep the dev-off / prod-on toggle (as
   in the foundation today) for dev parity; Compliance is turned on for the production buckets.
3. **DNS**: zone stays in `infra/foundation` (primary). The DNS API is account-level (not
   project-scoped), so its token is the accepted residual exposure documented in ADR-0017
   (operator-held; later a CI secret only for cert-manager DNS-01).

### Risks / assumptions

- The s3 **backend** credentials (state keys, via `AWS_*` env at init) are independent of the
  `aws` **provider** credentials (resource S3 keys, via `TF_VAR_*`). So `infra/backup` can write
  its state into the `cichlids-tfstate` project while managing buckets in the `cichlids-backup`
  project. Verified by configuration here; a real apply stays operator-run and gated.
- No real `tofu apply` runs in this task; verification is offline (`validate` + the static
  checks). The foundation/backup apply and the operator bootstrap remain the operator's gated
  out-of-band step.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Outcome

Split the single-project foundation into the three-project boundary from ADR-0017. The primary
project (`cichlids`) keeps the single cloud token; the immutable backups move into their own
`cichlids-backup` tier that holds no cloud token; the state stays in `cichlids-tfstate` under a
distinct key per tier.

### Changes

- **New `infra/backup/` tier** (`cichlids-backup`): `database_backup` + `media_master_backup`
  buckets, versioning + Object Lock **Compliance** + `prevent_destroy`. `aws` provider only —
  **no `hcloud` provider, no cloud token**. Backend state key `backup/terraform.tfstate` in the
  shared `cichlids-tfstate` bucket; backend creds (state keys) are independent of the tier's
  provider creds (backup-project S3 keys).
- **`infra/foundation/`**: removed the `backup` bucket + its variables/outputs; keeps node,
  volume, media-master (Governance), DNS zone, and the single `hcloud` cloud-token tier.
- **CI** (`infra-validate`): added backup `validate`, `infra/backup/guardrails_test.py`, and
  `tools/check-project-separation.py`.
- **Runbook** `infra/README.md`: rewritten for three projects, the project-based credential
  model, and the apply order (state bucket by hand -> backup tier -> foundation tier), each step
  with its expected result; GitHub-secrets list now excludes any backup/state cloud token.

### Red-Green evidence

- **Red first**: `tools/check-project-separation.py` failed (foundation still held the backup
  bucket; `infra/backup/` missing) and `infra/backup/guardrails_test.py` failed (buckets
  missing).
- **Green after**: all infra checks pass — `tofu fmt -check -recursive infra`, `tofu validate`
  (foundation + backup), foundation & backup guardrails, project-separation (self-test + real),
  remote-state (self-test + real, 2 tiers, distinct keys), secrets-scan, adr-index.

### Not done here (operator / gated)

- No real `tofu apply` — the foundation/backup apply and the three-project bootstrap remain the
  operator's out-of-band step (the runbook). DoD #3 "all CI green" is confirmed after push.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 At least one Given-When-Then acceptance criterion is specified
- [x] #2 A failing test was written first, then made to pass (red-green)
- [x] #3 All CI test suites are fully green
<!-- DOD:END -->
