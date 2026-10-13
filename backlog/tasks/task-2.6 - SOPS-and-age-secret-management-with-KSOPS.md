---
id: TASK-2.6
title: SOPS and age secret management with KSOPS
status: In Review
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-10-13 12:26'
labels:
  - infra
  - secrets
  - argocd
  - ci
milestone: m-0
dependencies:
  - TASK-2.1
references:
  - tools/check-secret-manifests.py
  - .sops.yaml
  - .github/workflows/ci.yml
parent_task_id: TASK-2
priority: high
ordinal: 8000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Establish the single secret-management method for both IaC and cluster secrets: **SOPS + age**,
decrypted in Argo CD via the **KSOPS** plugin (ADR-0010).

### Scope
- Provide `.sops.yaml` creation rules; wire the KSOPS plugin into the Argo CD install.
- Keep age private keys out of Git entirely (public key only in `.sops.yaml`; private key
  out-of-band and provisioned to the cluster as a secret by the operator).
- Ship a guard that fails CI if any plaintext Kubernetes Secret manifest exists and that
  round-trips a sops-encrypted fixture with a CI test key.

### Realises
- ADR-0010 (SOPS+age, KSOPS, no plaintext secrets in Git), ADR-0014 (no destroy/secret leakage
  into CI).

### Verifying artifact
- `.sops.yaml` + `tools/check-secret-manifests.py` + the SOPS and KSOPS round-trip + CI job `secrets-scan` (with gitleaks).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a plaintext Kubernetes Secret manifest committed anywhere, When the secrets-scan check runs, Then it fails and names the file
- [ ] #2 Given a sops-encrypted fixture and a CI test key, When the round-trip test runs, Then it decrypts and re-validates structurally and passes
- [ ] #3 Given Argo CD with the KSOPS plugin, When an encrypted manifest is reconciled, Then it is decrypted in-cluster without plaintext secrets in Git
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
- Red first: a guard script that fails on any plaintext Kubernetes Secret manifest in the tracked
  files (kind Secret with data or stringData that is not SOPS-encrypted) with fixtures, and a
  round-trip test that decrypts a SOPS-encrypted fixture with a CI test key and validates it
  structurally.

### Repository
- `.sops.yaml`: creation rules for encrypted manifests (for example `*.sops.yaml` under `apps/`
  and `clusters/`), encrypting only `data` and `stringData`; a separate rule for the test
  fixtures with the committed CI test key (test only, never for real secrets). The operator age
  public key is added as recipient once the operator provides it; until then the real rule is
  documented and absent.
- CI: the guard and the round-trip test run in the `secrets-scan` job next to gitleaks.

### Cluster
- Argo CD with KSOPS: the app tier installs Argo CD through a kustomization that adds the KSOPS
  binary to the repo server (init container with a pinned KSOPS release), enables Kustomize
  alpha plugins and exec plugins, and mounts the age key from the secret `sops-age` in the
  `argocd` namespace. The secret is created by the operator out of band, never by CI.
- An encrypted example manifest proves the reconciliation path once the operator key exists
  (AC #3, operator check).

### Docs
- `infra/README.md`: age key generation, public key into `.sops.yaml`, private key into the
  cluster secret and the password manager, rotation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- `tools/check-secret-manifests.py`: fails on any tracked Kubernetes Secret in YAML or JSON whose
  `data` or `stringData` is not SOPS-encrypted (also duplicate mapping keys), naming the file.
- `.sops.yaml`: the only active creation rule covers the public CI fixture, encrypted with a
  committed test-only age key (gitleaks allowlist limited to that key file). The rules for real
  secrets (`apps/` and `clusters/` `*.sops.yaml` with `data`/`stringData`, `infra/` `*.sops.*`) are
  documented in `infra/README.md` and are added with the operator public key.
- Round-trip test: decrypt the fixture with the test key, validate it, render it through KSOPS;
  a missing key is rejected; recipient isolation tested.
- Argo CD: `install-argocd.sh` applies a kustomization (upstream v3.5.5 plus KSOPS v4.5.1, init
  container pinned by digest, Kustomize flags `--enable-alpha-plugins --enable-exec`, optional
  mount of secret `sops-age` with `SOPS_AGE_KEY_FILE`); the repo server starts without the secret.
- CI `secrets-scan` runs the guard, the round-trip and gitleaks.

### Verified by the agent (Codex)
- Guard red with a plaintext fixture, then 137 tracked YAML/JSON files OK; 12 tests OK; KSOPS
  render; manifests 29 valid; app tier validate and guardrails; gitleaks over tracked files and
  the full history without findings.

### Operator checks
- Generate the age key, give the public key (`age1...`) to the agent for `.sops.yaml`, keep
  `keys.txt` in the password manager.
- After the app tier bootstrap: create the secret `sops-age` in `argocd` on the node, restart the
  repo server, reconcile an encrypted manifest (AC #3).
<!-- SECTION:NOTES:END -->
