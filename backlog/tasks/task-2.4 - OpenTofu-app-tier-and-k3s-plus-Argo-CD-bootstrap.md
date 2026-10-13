---
id: TASK-2.4
title: OpenTofu app tier and k3s plus Argo CD bootstrap
status: In Progress
assignee: []
created_date: '2026-06-03 16:00'
updated_date: '2026-10-13 07:24'
labels:
  - infra
  - opentofu
  - k3s
  - argocd
milestone: m-0
dependencies:
  - TASK-2.2
  - TASK-2.27
  - TASK-2.28
  - TASK-3.29
  - TASK-3.30
  - TASK-3.31
  - TASK-3.32
  - TASK-3.33
  - TASK-3.34
  - TASK-3.35
references:
  - infra/app/guardrails_test.py
  - .github/workflows/infra-app.yml
  - policy/destroy_gate_test.rego
parent_task_id: TASK-2
priority: high
ordinal: 6000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Define the ephemeral (**app**) OpenTofu tier that routine automation may apply.

### Scope
- Install single-node **k3s** on the existing foundation node **over SSH** (the bundled Traefik
  disabled), and hand off to an **Argo CD** install so the cluster reconciles the rest from Git.
  The node is created without cloud-init and is protected against replacement (ADR-0014), so the
  app tier installs and upgrades k3s on the running node.
- This tier must reference **none** of the foundation tier's data resources (separate state,
  Layer 2 of ADR-0014) and holds no cloud token: the node address and its host key are inputs.
- The CI infra pipeline runs `tofu plan` as a pull-request check and `tofu apply` after merge
  using the routine credentials (ADR-0005): a dedicated CI SSH key, the state backend key and the
  app state passphrase.

### Realises
- ADR-0010 (provisioning split, k3s+Argo bootstrap, Traefik disabled), ADR-0014 (Layer 2
  state/credential separation, Layer 3 gate on the app plan).

### Verifying artifact
- `infra/app` + a check asserting the app tier resolves without referencing any foundation
  resource address.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given infra/app, When tofu fmt -check and tofu validate run, Then both pass
- [ ] #2 Given the app tier, When checked, Then it references no foundation resource address and holds no destroy-capable credential
- [ ] #3 Given a merge to main touching infra/app, When the infra pipeline runs, Then tofu plan ran as a PR check and tofu apply runs post-merge with the routine token
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
- Red first: `infra/app/guardrails_test.py` and a destroy-gate unit test for the app tier fail
  before the tier exists; then the tier makes them pass.

### infra/app (OpenTofu, own state)
- `backend.tf`: S3 backend in `cichlids-platform-tfstate`, key `app/terraform.tfstate`, native
  encryption with its own passphrase variable (`state_encryption_passphrase`), never the platform
  passphrase.
- `versions.tf`: no cloud provider; only built-in `terraform_data`.
- `variables.tf`: `node_ipv4`, `node_host_key` (pinned SSH host key), `ssh_private_key`
  (sensitive), `ssh_user`, `k3s_version` (v1.36.5+k3s1), `argocd_version` (v3.5.5),
  `repo_url`, `repo_revision`, `cluster_path` (clusters/cichlids).
- `main.tf`: `terraform_data.k3s` (triggers: version, script hash) uploads and runs
  `scripts/install-k3s.sh`; `terraform_data.argocd` (depends on k3s; triggers: version, script
  and root manifest hash) runs `scripts/install-argocd.sh` and applies the root Application.
- Scripts are idempotent: k3s from the tagged `install.sh` of the pinned release with
  `--disable traefik`, skipped when the running version matches; Argo CD from the pinned
  `install.yaml` with server-side apply, then wait for rollout.

### Git side of the hand-off
- `clusters/cichlids/root.yaml`: root Application pointing at `clusters/cichlids/apps`
  (automated sync, self-heal, no prune), filled by TASK-2.7.
- `clusters/cichlids/apps/kustomization.yaml`: empty resource list.

### Checks and gate
- `infra/app/guardrails_test.py`: no `data` blocks, no `terraform_remote_state`, no cloud
  provider, no token variable, no foundation resource address, remote backend with the app key,
  pinned host key used by every connection.
- `tools/check-remote-state-backend.py`: app tier included.
- `policy/destroy_gate.rego`: `terraform_data` classified ephemeral, with unit tests.
- CI `infra-validate`: validate the app tier and run its guardrails.
- New workflow `infra-app.yml`: plan plus Conftest gate on pull requests touching `infra/app`,
  `clusters/` or `policy/`; on push to main plan, gate and apply. Serialized, no fork PRs,
  credentials from GitHub secrets and variables.

### Docs
- `infra/tofu.sh` and `cichlids.env.example`: tier `app` with its own credentials.
- `infra/README.md`: app tier runbook (CI key, host key, secrets, first apply, checks).
- ADR-0010: cluster bootstrap over SSH from the app tier instead of cloud-init.

### Operator steps (not doable by the agent)
- Create the CI key pair, authorise it on the node, set GitHub secrets and variables, run the
  first apply, observe AC #3 on a real PR and merge.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- `infra/app/` (own state `app/terraform.tfstate`, own passphrase, no cloud provider):
  `terraform_data.k3s` and `terraform_data.argocd` upload and run
  `scripts/install-k3s.sh` (k3s v1.36.5+k3s1, Traefik disabled) and `scripts/install-argocd.sh`
  (Argo CD v3.5.5, root Application from `clusters/cichlids/root.yaml`) over SSH with the pinned
  host key. Inputs: node address, host key, CI private key; no repo variables, the root manifest
  carries repo URL, revision and path.
- `clusters/cichlids/root.yaml` and an empty `clusters/cichlids/apps/kustomization.yaml`.
- Destroy gate: `terraform_data` is ephemeral, so a version bump (replace) passes while a
  destroy of a critical resource in the same plan is denied.
- CI: `infra-validate` validates the app tier and runs its guardrails; workflow `infra-app`
  plans and gates on pull requests and applies after merge to main.
- `infra/tofu.sh app`, credential template and the runbook section "App (ephemeral) tier" in
  `infra/README.md`; ADR-0010 describes the SSH bootstrap.

### Verified by the agent (static only)
- `tofu fmt -check`, `init -backend=false` and `validate` for all three tiers; app, platform and
  backup guardrails; project separation and remote state checks with self-tests; `conftest
  verify` 7 of 7; `bash -n` on the scripts; story-test binding.

### Operator checks (open)
- Runbook steps 1 to 7 in `infra/README.md`, then a pull request touching `infra/app` shows the
  plan check, and the merge runs the apply (AC #3). AC #1 and #2 are observed on the same run.
<!-- SECTION:NOTES:END -->
