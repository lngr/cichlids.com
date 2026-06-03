---
id: TASK-2
title: Foundation and strict GitOps platform
status: In Progress
assignee: []
created_date: '2026-06-03 15:59'
labels:
  - epic
  - infra
  - gitops
milestone: m-0
dependencies: []
priority: high
ordinal: 2000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Epic. Stand up the complete, real GitOps platform from day one so later feature work merely fills in workloads — no big-bang migration. OpenTofu provisions the Hetzner node, object storage (media-master + backup buckets) and the Hetzner DNS zone, and bootstraps single-node k3s + Argo CD; from then on Argo CD continuously reconciles everything from Git (ADR-0010).

Binding from day one: layered safeguards against destructive infrastructure changes (ADR-0014) and PostgreSQL continuous-archiving/PITR backup via CloudNativePG with a first tested restore (ADR-0013, RPO<=5min/RTO<=4h). Global delivery and cost stance per ADR-0009 (Hetzner object storage as master, Cloudflare R2+CDN for image serving later). CI green gate and infra pipeline on GitHub Actions (ADR-0005).

Environments on a single k3s node (cost; splittable later): persistent prod and staging plus ephemeral per-PR preview environments (Argo CD ApplicationSet PR generator), promotion flow feature-branch/PR -> preview URL -> merge to main -> staging (auto) -> deliberate promotion -> prod. A placeholder workload proves the full preview->staging->prod path over TLS before real application code lands.

Children: the technical subtasks (toolchain, OpenTofu foundation/app tiers, Conftest plan gate, SOPS+age, Argo CD app-of-apps, ingress/cert-manager, CloudNativePG backup, restore drill, bootstrap runbook) and the one user-observable vertical-slice story (first green end-to-end smoke path). Operator (repository owner) performs all account/token creation, git commits/pushes/merges and the initial tofu apply; the destroy-capable break-glass credential stays out-of-band (ADR-0014).
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
