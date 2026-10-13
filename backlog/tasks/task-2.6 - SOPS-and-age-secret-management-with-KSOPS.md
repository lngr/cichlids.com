---
id: TASK-2.6
title: SOPS and age secret management with KSOPS
status: In Progress
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-10-13 12:15'
labels:
  - infra
  - secrets
  - argocd
  - ci
milestone: m-0
dependencies:
  - TASK-2.1
references:
  - .sops.yaml
  - tools/check-no-plaintext-secrets.mjs
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
- `.sops.yaml` + `tools/check-no-plaintext-secrets` + CI job `secrets-scan`.
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
