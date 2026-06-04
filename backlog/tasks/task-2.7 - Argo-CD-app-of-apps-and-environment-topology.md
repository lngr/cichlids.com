---
id: TASK-2.7
title: Argo CD app-of-apps and environment topology
status: To Do
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-06-04 12:57'
labels:
  - infra
  - argocd
  - gitops
milestone: m-0
dependencies:
  - TASK-2.4
  - TASK-2.6
references:
  - clusters/cichlids/
parent_task_id: TASK-2
priority: high
ordinal: 9000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Define the Argo CD bootstrap and the environment topology in Git.

### Scope
- An **app-of-apps** root Application and ApplicationSets that materialise the persistent
  **staging** and **prod** environments as Kustomize overlays.
- Ephemeral **preview** environments per pull request via the ApplicationSet PullRequest
  generator (one `pr-<n>` namespace each, torn down on merge/close).
- Previews derive from a single **preview overlay** (no per-branch config drift, ADR-0010),
  constrained on the single node by a ResourceQuota, a concurrency cap, and auto-expiry.
- Stateful Applications carry `Prune=false` and finalizers so reconciliation never deletes data
  (ADR-0014 Layer 1).

### Realises
- ADR-0010 (Argo CD app-of-apps, overlays not branches; staging image-tag bump via PR), ADR-0014.

### Verifying artifact
- `clusters/cichlids/` rendering cleanly under kustomize/helm + schema validation; CI job
  `manifests`.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given clusters/cichlids, When kustomize build / helm template runs for the root app and ApplicationSets, Then output renders and passes schema validation (kubeconform / dry-run)
- [ ] #2 Given a stateful Application, When inspected, Then it sets Prune=false and a finalizer
- [ ] #3 Given the preview ApplicationSet, When a pull request is open, Then exactly one pr-<n> environment is defined from the shared preview overlay, and none remains after merge/close
- [ ] #4 Given the single node, When previews are defined, Then a ResourceQuota and a concurrency cap bound their footprint
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
