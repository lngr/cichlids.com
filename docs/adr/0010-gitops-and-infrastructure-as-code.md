# ADR-0010: GitOps and Infrastructure as Code

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

The system spreads across several cloud building blocks
([ADR-0009](0009-hosting-and-global-delivery.md)): a serving object store and CDN, an
authoritative master copy of the originals, a compute environment, a PostgreSQL database, and a
self-operated Keycloak. If this were set up and changed manually via consoles ("click-ops"), it
would not be reproducible, not reviewable, and its change history not traceable — a real risk
with fully agent-supported development.

A binding decision is needed on **how** infrastructure, configuration, and workloads are
defined, changed, and rolled out — and on **how strictly** the running state is kept in sync
with Git.

## Decision

The project runs on **strict, reconciling GitOps on Kubernetes** from the start. **Git is the
source of truth**, and an in-cluster controller **continuously reconciles** the cluster to Git
and **heals drift** — not a one-shot pipeline apply. Changes flow exclusively through pull
requests.

| Aspect | Commitment |
|---|---|
| **Runtime** | **Kubernetes**, as a **single-node k3s** cluster on **Hetzner Cloud** in a **US location** (API latency, per [ADR-0009](0009-hosting-and-global-delivery.md)). Expandable to multi-node / HA (3 control-plane) **without re-architecture**, but only **on massive growth** — the predecessor ran for ~20 years on a single host, so single-node is a deliberate, long-lived start. |
| **Cluster bootstrap** | A **lean single-node k3s** installed by **OpenTofu + cloud-init**; a fuller module (e.g. `kube-hetzner`) is adopted only when moving to multi-node. |
| **Provisioning split** | **OpenTofu** provisions the cloud resources (Hetzner node(s), network, the Hetzner Object Storage bucket, DNS) **and bootstraps** the cluster + the reconciler. From then on, **everything inside the cluster is reconciled from Git.** |
| **Reconciler** | **Argo CD** (app-of-apps / ApplicationSets), chosen for its visibility and ubiquity. |
| **Ingress & TLS** | **ingress-nginx + cert-manager** (Let's Encrypt); the k3s-bundled Traefik is disabled. |
| **Image delivery** | CI builds and pushes the image. **Staging** is updated by an auto-merged pull request that bumps the image tag in the `staging` overlay; **prod** is promoted deliberately by **cutting a git tag / publishing a GitHub release**, not by a promotion PR. Argo CD reconciles the resulting Git change. No automatic write-back beyond these. |
| **Declarative in Git** | Workloads (.NET API, Keycloak, video transcoder, AI agents), cluster add-ons, the **Keycloak realm** (e.g. via `keycloak-config-cli`), and **DB schema migrations**. |
| **Stateful data** | Postgres runs **in-cluster via the CloudNativePG operator** — declarative replicas and PITR backups to object storage, realizing [ADR-0013](0013-backup-and-disaster-recovery.md). (A managed external database remains a fallback.) |
| **Secrets** | **SOPS + age** — one method for **both** IaC and cluster secrets; decrypted in Argo CD via the KSOPS plugin. No plaintext secrets in Git. |
| **Repository layout & promotion** | A **monorepo**: `infra/` (OpenTofu: `platform/` for the cichlids cloud resources, `backup/` for the immutable backups, later `app/` for the ephemeral tier, per [ADR-0014](0014-safeguards-against-destructive-infrastructure-changes.md) and [ADR-0017](0017-hetzner-project-separation.md)), `clusters/<name>/` (Argo CD bootstrap + app-of-apps), `apps/<service>/` (Kustomize **base** + per-environment **overlays** `staging`/`prod`). CI builds/pushes the image; for **staging** it opens an auto-merged PR that bumps the image tag in the staging overlay, while **prod** is promoted by **cutting a git tag / GitHub release** (no promotion PR). Argo reconciles the resulting Git change. Environments are Kustomize overlays (not branches). |

## Considered Options

- **Strict, reconciling GitOps on Kubernetes (chosen).**
  - *Pro:* continuous reconciliation and self-healing of drift; auditable, PR-driven changes; a
    cloud-native capability worth building; scales out (multi-node/HA) without re-architecture;
    CloudNativePG folds backup/replication into the same declarative model.
  - *Con:* a cluster to operate even at one node; higher cost and complexity than a single box;
    a single node has no high availability until expanded; Argo CD's secrets and image-update
    handling need add-ons.
- **Pragmatic GitOps: IaC + CI/CD + declarative config, no cluster.**
  - *Pro:* simplest and cheapest; a single box, no cluster ops; still reproducible and
    PR-reviewed.
  - *Con:* `apply` is pipeline-driven, so there is **no continuous reconciliation or
    self-healing** of drift; least cloud-native depth. A reasonable alternative, not chosen
    because the reconciling model and the capability it builds are explicitly wanted.
- **Managed Kubernetes elsewhere** (free control plane at another provider).
  - *Pro:* no control-plane operations.
  - *Con:* gives up provisioning the cluster ourselves on Hetzner and the colocated cheap
    storage; kept as a fallback if self-managed ops become a burden.
- **Full infrastructure reconciled in-cluster** (Crossplane / Cluster API).
  - *Pro:* even cloud resources become continuously reconciled CRDs — the purest GitOps.
  - *Con:* significant added complexity and bootstrapping; deliberately deferred as a possible
    later step once the cluster-based model is solid.
- **Click-ops (manual setup via consoles).**
  - *Con:* not reproducible, not reviewable, no history; unacceptable for traceable,
    agent-supported work.

## Rationale

GitOps makes the whole environment reproducible, reviewable, and steerable via pull requests —
exactly what gives safety at a high volume of agent-generated change. We go beyond the pragmatic
variant to the **strict, reconciling** model because continuous reconciliation and self-healing
of drift are genuinely valuable operationally, because the project is cloud-based and
well-scoped to build this capability, and because it scales out cleanly under load. Starting
with a **single-node k3s** keeps cost and operational load modest now while leaving a clean path
to multi-node/HA. **Argo CD** is chosen over Flux for this first build because its UI makes
reconciliation and drift **visible**, which both speeds operation and accelerates the capability
being built. **CloudNativePG** lets the hardest operational risk — database backup and
replication ([ADR-0013](0013-backup-and-disaster-recovery.md)) — be expressed in the same
declarative model rather than bolted on.

## Consequences

- **Positive:** continuously reconciled, self-healing, auditable infrastructure; one declarative
  model for workloads, data, and config; scales out without re-architecture; a transferable
  cloud-native skill.
- **Negative / Trade-offs:** a Kubernetes cluster to operate even at one node; more moving parts
  and higher cost than a single box; a single node has **no HA** until expanded; Argo CD's
  secrets and image-update handling need add-ons.
Safeguards against destructive infrastructure changes (e.g. an automated agent accidentally
destroying the cluster or data) are **binding** and specified in
[ADR-0014](0014-safeguards-against-destructive-infrastructure-changes.md).

- **Open items:** none — the GitOps setup is fully decided here and in ADR-0014.

## References

- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0005: CI Platform and Green Gate](0005-ci-platform.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0013: Backup and Disaster Recovery](0013-backup-and-disaster-recovery.md)
- [ADR-0014: Safeguards Against Destructive Infrastructure Changes](0014-safeguards-against-destructive-infrastructure-changes.md)
