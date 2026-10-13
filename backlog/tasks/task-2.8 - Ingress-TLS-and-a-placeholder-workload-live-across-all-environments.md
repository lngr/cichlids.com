---
id: TASK-2.8
title: 'Ingress, TLS and a placeholder workload live across all environments'
status: In Progress
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-10-13 15:01'
labels:
  - infra
  - ingress
  - tls
  - gitops
milestone: m-0
dependencies:
  - TASK-2.7
references:
  - apps/cert-manager/
  - apps/placeholder/
parent_task_id: TASK-2
priority: high
ordinal: 10000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Bring the platform's edge online and prove the full delivery path before any application code
exists.

### Scope
- Use the **Traefik** bundled with k3s as ingress controller and deploy **cert-manager** (Let's Encrypt) via Kustomize base+overlays.
- Certificates per host through cert-manager **HTTP-01** (Let's Encrypt), so the cluster holds no
  Hetzner Cloud token; a wildcard A record `*.dev.cichlids.com` so preview hosts resolve without
  per-PR DNS changes.
- Deploy a minimal **placeholder** workload (e.g. a static page) into preview, staging and prod
  so each environment is reachable over TLS at its hostname.

### Why
- The no-big-bang proof that preview→staging→prod, ingress, TLS and Argo reconciliation all work
  end to end.

### Realises
- ADR-0010 (Traefik + cert-manager) and the environment topology.

### Verifying artifact
- the Traefik configuration, `apps/cert-manager`, `apps/placeholder` rendering + the live TLS URLs.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the manifests, When kustomize build runs for the Traefik configuration, cert-manager and placeholder across overlays, Then they render and pass schema validation
- [ ] #2 Given the applied cluster, When a user opens the staging and prod placeholder URLs, Then each serves the placeholder over valid TLS
- [ ] #3 Given an open pull request, When the operator opens pr-<n>.dev.cichlids.com, Then the placeholder is reachable over a valid TLS certificate for that host
- [ ] #4 Given a merged/closed pull request, When reconciliation settles, Then the preview environment is gone
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
- Red first: extend `tools/check-cluster-manifests.sh` (or its test) with expectations for the
  new services before they exist.

### Services (Kustomize base plus overlays, discovered by the TASK-2.7 generators)
- Traefik bundled with k3s as the node edge (ports 80 and 443 through k3s ServiceLB), configured
  from Git; cluster-scoped add-ons deployed once (decide where the cluster-wide add-ons live in the
  topology, for example an `addons` ApplicationSet or Application, and document it).
- `apps/cert-manager`: pinned upstream release; ClusterIssuers for Let's Encrypt staging and
  production with the HTTP-01 solver through Traefik; no cloud token anywhere.
- `apps/placeholder`: a minimal static page (pinned small image), overlays staging
  (`staging.cichlids.com`), prod (`cichlids.com`) and preview (`pr-<number>.dev.cichlids.com`),
  each with an Ingress and a cert-manager certificate annotation.

### DNS (platform tier, applied by the operator)
- Records for `staging.cichlids.com`, `cichlids.com` and the wildcard `*.dev.cichlids.com` to the
  node address in `infra/platform/dns.tf`; guardrails and destroy gate stay green.

### Operator checks
- Platform apply for the records, then the URLs over valid TLS (AC #2, #3) and the preview removal
  (AC #4).
<!-- SECTION:PLAN:END -->
