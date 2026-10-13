---
id: TASK-2.8
title: 'Ingress, TLS and a placeholder workload live across all environments'
status: In Review
assignee: []
created_date: '2026-06-03 16:01'
updated_date: '2026-10-13 15:13'
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
  - tools/check-cluster-manifests.sh
  - apps/traefik/overlays/cluster/config.yaml
  - apps/placeholder/base/deployment.yaml
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- Edge: the Traefik bundled with k3s; ports 80 and 443 through k3s ServiceLB; its HelmChartConfig
  (`apps/traefik/overlays/cluster/config.yaml`) is reconciled by Argo CD in the addons project and
  redirects HTTP to HTTPS except the ACME HTTP-01 challenge path.
- k3s settings in `/etc/rancher/k3s/config.yaml`, written by `install-k3s.sh`; a differing config or
  version reruns the installer and restarts k3s, an identical one changes nothing; a marker resumes
  an interrupted application.
- cert-manager v1.21.2 (`apps/cert-manager`, addons project, `cluster` overlay) with ClusterIssuers
  for Let's Encrypt staging and production, HTTP-01 through ingress class `traefik`; ACME email
  placeholder `acme@cichlids.com`; no cloud token in the cluster.
- Placeholder: BusyBox 1.37.0-musl pinned by digest, non-root, read-only root filesystem;
  overlays staging (`staging.cichlids.com`), prod (`cichlids.com`) and preview
  (`pr-<number>.dev.cichlids.com`, host set by the preview ApplicationSet), each with Ingress and
  a production certificate.
- DNS (platform tier): A records for the apex, `staging` and `*.dev` to the node address.
- ADR-0010 names Traefik and HTTP-01 per host.

### Verified by the agent (Codex)
- Manifest check red, then 149 resources valid; the bundled chart renders; a local Traefik run
  answers 301 for normal requests and 200 for the ACME path; app and platform validate,
  guardrails, destroy gate, secret guard, ADR index, story binding.
- Not verified: live rollout, ServiceLB port binding, real certificate issuance.

### Operator checks
- Confirm or replace the ACME email `acme@cichlids.com` (monitored address).
- Platform apply for the three DNS records, then public resolution and ports 80 and 443.
- After the app tier bootstrap: Argo CD syncs addons, issuers and certificates; HTTPS for staging,
  prod and one preview (AC #2, #3); the preview disappears after close (AC #4).
<!-- SECTION:NOTES:END -->
