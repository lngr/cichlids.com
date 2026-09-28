---
id: TASK-2.22
title: 'Collapse to two projects: CI-managed cichlids plus isolated immutable backups'
status: In Progress
assignee:
  - '@claude'
created_date: '2026-06-09 15:01'
updated_date: '2026-06-09 19:33'
labels:
  - infra
  - opentofu
  - security
milestone: m-0
dependencies:
  - TASK-2.18
references:
  - tools/check-project-separation.py
parent_task_id: TASK-2
priority: high
ordinal: 24000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Reduce the project model to the only isolation that carries weight: the irreplaceable backups. Everything else is recoverable cattle and is managed by CI/GitOps in the cichlids project.

### Target (two projects)
- cichlids: all live infrastructure (node, DB volume, media-master bucket, DNS zone, and later the app tier) plus its own OpenTofu state bucket. Managed by CI/GitOps via tofu, with prevent_destroy + delete_protection as accident guards. Holds the cloud Read & Write token and the primary object-storage key (used for both the media-master bucket and the state backend).
- cichlids-backup: the immutable backups (Object Lock Compliance) plus an unmanaged bucket holding the backup tier's OpenTofu state. No cloud token; its object-storage key is operator-only and never a CI secret.
- Removed: the cichlids-tfstate project and the cichlids-ci project.

### Why this is safe
The data is recoverable from the immutable backups, so a mistake in cichlids (even a token-level one) is a recoverable outage, not data loss. Only the backups need isolation, and they keep it (no cloud token reaches cichlids-backup, Compliance Object Lock blocks deletion). prevent_destroy/delete_protection remain as accident prevention within cichlids.

### Scope
- Re-tier: infra/foundation/ (node, volume, media-master, DNS) becomes the CI-managed platform tier infra/platform/, state bucket in cichlids. infra/backup/ keeps its state bucket in cichlids-backup.
- Credentials: drop CICHLIDS_S3_STATE_* and CICHLIDS_S3_CI_*; the cichlids key (CICHLIDS_S3_PRIMARY_*) serves the cichlids state backend, the backup key (CICHLIDS_S3_BACKUP_*) serves the backup state backend.
- Wrapper infra/tofu.sh, cichlids.env.example, the project-separation and remote-state checks, guardrails, ADR-0014/0016/0017, and the README all follow the two-project model.
- Remove the fixture-based infra-policy CI job, its fixtures and tools/destroy-protection-demo.sh (theater); keep the destroy_gate.rego policy + its unit tests. The real plan-diff gate (Layer 3) runs on the CI-apply pipeline's tofu plan when the app/CI-apply tier is built.
- Migrate the already-applied live state to the new buckets with no resource destroyed, then delete the old cichlids-tfstate and cichlids-ci projects.

### Supersedes / adjusts
- Supersedes TASK-2.20 (cichlids-ci). Adjusts TASK-2.21 (Layer 2 now means: only the backups are isolated). TASK-2.18 (DNS to hcloud) stays valid.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the infrastructure, When reviewed, Then there are exactly two cloud projects (cichlids holding all live infra plus its state bucket; cichlids-backup holding the immutable backups plus the backup state bucket); the cichlids-tfstate and cichlids-ci projects no longer exist
- [ ] #2 Given the credential model, When the cichlids object-storage key is used against the cichlids-backup buckets (backups and backup state), Then access is refused, and cichlids-backup holds no cloud token; the backup key is never a CI secret
- [ ] #3 Given the platform tier (node, volume, media-master, DNS) in cichlids, When tofu validate and the guardrails run, Then the resources carry prevent_destroy/delete_protection and the project-separation and remote-state checks assert the two-project boundary, with self-tests that fail before the rule and pass after
- [ ] #4 Given ADR-0014/0016/0017 and the README, When reviewed, Then they describe the two-project model and the revised layers, and the fixture-based infra-policy job and the offline demo are removed
- [ ] #5 Given the live deployment, When the two-project model is applied, Then the platform and backup tiers are live in the cichlids and cichlids-backup projects and the old cichlids-tfstate and cichlids-ci projects are removed
<!-- AC:END -->



## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Proposed names (operator may change): tier dir infra/platform/; platform state bucket cichlids-platform-tfstate (in cichlids); backup state bucket cichlids-backup-tfstate (in cichlids-backup).

### Phase 1 - code, checks, docs (agent, test-first, no apply)
1. Re-tier infra/foundation/ -> infra/platform/ (node, volume, media-master, DNS). backend.tf: bucket cichlids-platform-tfstate (in cichlids), key platform/terraform.tfstate. Update guardrails_test.py path + Story tags.
2. infra/backup/backend.tf: bucket cichlids-backup-tfstate (in cichlids-backup), key backup/terraform.tfstate.
3. infra/tofu.sh: tiers platform|backup. platform backend+provider creds = CICHLIDS_S3_PRIMARY_*, plus hcloud token + ssh + passphrase; backup backend+provider creds = CICHLIDS_S3_BACKUP_*, no cloud token. Drop CICHLIDS_S3_STATE_* and CICHLIDS_S3_CI_*.
4. cichlids.env.example: keep HCLOUD_TOKEN, S3_PRIMARY_*, S3_BACKUP_*, STATE_PASSPHRASE, SSH_PUBLIC_KEY, S3_ENDPOINT/REGION; drop S3_STATE_* and S3_CI_*.
5. tools/check-project-separation.py: two-project assertions (backup tier no hcloud provider/token, Compliance backup buckets with prevent_destroy; platform tier has hcloud + media-master, no backup bucket) + self-test.
6. tools/check-remote-state-backend.py: tiers platform, backup; each has a remote backend; state bucket is not a managed bucket; the backup state bucket differs from the platform state bucket (trust boundary). Replace the cichlids-ci fixture; new self-test.
7. tools/verify-project-isolation.sh: update to the cichlids key (CI-held) cannot reach the cichlids-backup buckets (backups + backup state).
8. ci.yml: remove the infra-policy job, the fixtures and tools/destroy-protection-demo.sh; keep policy/destroy_gate.rego + destroy_gate_test.rego and run conftest verify as one step in infra-validate; fix foundation->platform paths.
9. ADR-0017 rewrite to two projects (only the backups need isolation; everything in cichlids is recoverable cattle). ADR-0016 two state backends. ADR-0014 revised layers (Layer 3 plan-diff gate runs on the CI-apply pipeline's tofu plan).
10. README: two-project HOWTO (platform tier CI-managed; backup tier operator out-of-band; one state bucket per project).
11. Mark TASK-2.20 superseded; reword TASK-2.21 AC #2 (boundary = backups isolated).
12. All gates green: fmt, validate platform+backup, guardrails, project-separation + remote-state self-tests, conftest verify, secrets-scan, adr-index, story-binding.

### Phase 2 - provision new state buckets (operator, console)
- cichlids: create bucket cichlids-platform-tfstate (versioned, private, Object Lock off).
- cichlids-backup: create bucket cichlids-backup-tfstate (versioned, private, Object Lock off).

### Phase 3 - migrate the live state (operator, no resource destroyed)
For platform and backup, move state from the old cichlids-tfstate bucket to the new bucket with the new backend.tf in place: tofu init -migrate-state (read old, write new), then plan must show zero changes. Keep the old bucket until both tiers plan clean (state is versioned, so recoverable). Exact commands given at execution.

### Phase 4 - decommission old projects (operator)
After both tiers plan = no changes against the new buckets: delete the old cichlids-tfstate bucket and project, and the cichlids-ci project + bucket.

### Phase 5 - re-verify layers (TASK-2.21) against the two-project model
Layer 1 unchanged (verified). Layer 2 re-verify via the updated isolation script (cichlids key cannot reach cichlids-backup). Layer 3 deferred to the CI-apply tier. Layer 4 Object Lock immutability on a backup object.

### Risks
- State migration is the delicate step: always confirm plan = no changes before deleting the old bucket; the old bucket stays until verified; state versioning is the safety net.
- New bucket names avoid global-uniqueness collision with the existing cichlids-tfstate.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Delivered
- Two projects: cichlids (platform tier: node, DB volume, media-master, DNS, plus the cichlids-platform-tfstate state bucket) and cichlids-backup (immutable backup buckets plus the cichlids-backup-tfstate state bucket). The cichlids-tfstate and cichlids-ci projects are gone.
- Re-tier infra/foundation -> infra/platform; per-tier backends in their own project; wrapper + cichlids.env.example on two projects (CICHLIDS_S3_STATE_*/CICHLIDS_S3_CI_* removed; the cichlids key serves platform provider+backend, the backup key serves backup provider+backend).
- check-project-separation, check-remote-state-backend, verify-project-isolation.sh on the two-project boundary; ADR-0014/0016/0017 and the README on the two-project model.
- Hetzner Object Storage does not implement bucket tagging (PutBucketTagging 501); the aws_s3_bucket resources carry no tags and are critical to the policy gate by type. The dead label locals were removed.
- The live deployment was rebuilt fresh into the two-project model (the throwaway resources from the earlier single-state-project layout were removed first).

### Verification
- Static gates green: fmt, validate (platform + backup), guardrails, project-separation, remote-state, conftest verify, secrets-scan, adr-index, story-binding.
- Live: the platform and backup tiers apply cleanly; the ADR-0014 layers are verified in TASK-2.21 (Layer 1 prevent_destroy/delete_protection, Layer 2 project isolation, Layer 4 Compliance immutability). Layer 3 (plan-diff gate) wires into the CI-apply pipeline with the app tier (TASK-2.4).
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 The static checks are red before the rule and green after (project-separation, remote-state)
- [ ] #3 The two-project model is applied and live (platform + backup), verified by the ADR-0014 layer checks (operator-observed)
- [ ] #4 All CI test suites are green
<!-- DOD:END -->
