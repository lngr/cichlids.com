---
id: TASK-2.6
title: SOPS and age secret management with KSOPS
status: To Do
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-06-05 10:25'
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
