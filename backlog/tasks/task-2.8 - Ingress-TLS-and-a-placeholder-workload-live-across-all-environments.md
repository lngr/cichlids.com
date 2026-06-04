---
id: TASK-2.8
title: 'Ingress, TLS and a placeholder workload live across all environments'
status: To Do
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-06-04 12:57'
labels:
  - infra
  - ingress
  - tls
  - gitops
milestone: m-0
dependencies:
  - TASK-2.7
references:
  - apps/ingress-nginx/
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
- Deploy **ingress-nginx** and **cert-manager** (Let's Encrypt) via Kustomize base+overlays.
- A wildcard `*.dev.cichlids.com` certificate issued through cert-manager **DNS-01** using the
  Hetzner DNS solver, and a wildcard A record so preview hosts resolve without per-PR DNS changes.
- Deploy a minimal **placeholder** workload (e.g. a static page) into preview, staging and prod
  so each environment is reachable over TLS at its hostname.

### Why
- The no-big-bang proof that preview→staging→prod, ingress, TLS and Argo reconciliation all work
  end to end.

### Realises
- ADR-0010 (ingress-nginx + cert-manager, Traefik disabled) and the environment topology.

### Verifying artifact
- `apps/ingress-nginx`, `apps/cert-manager`, `apps/placeholder` rendering + the live TLS URLs.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the manifests, When kustomize build runs for ingress-nginx, cert-manager and placeholder across overlays, Then they render and pass schema validation
- [ ] #2 Given the applied cluster, When a user opens the staging and prod placeholder URLs, Then each serves the placeholder over valid TLS
- [ ] #3 Given an open pull request, When the operator opens pr-<n>.dev.cichlids.com, Then the placeholder is reachable over the wildcard TLS certificate
- [ ] #4 Given a merged/closed pull request, When reconciliation settles, Then the preview environment is gone
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
