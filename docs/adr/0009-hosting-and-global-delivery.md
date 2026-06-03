# ADR-0009: Hosting and Global Delivery

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

The application ([ADR-0001](0001-purpose-and-scope.md)) serves a **global user base with a
clear US focus** (approx. 90% USA, further members in, among others, Japan and Europe) and
manages a **large media stock**: roughly 235 GB of image originals (about 185,000 images) and,
going forward, **video** — which is markedly larger per item and far more egress-heavy than
photos and is therefore hosted on a separate channel ([ADR-0012](0012-video-transcoding-and-adaptive-delivery.md)).
The legacy application was hosted in Germany and was noticeably slow for US users.

Two requirements thus stand in the foreground and seemingly in conflict: **globally low
latency** (fast, responsive gallery browsing) and **low cost** (no monetization in this
stage; the operator pays themselves). The decisive cost driver for large image volumes is
**not storage but egress** (outbound data transfer). A commitment to the delivery and hosting
architecture that satisfies both requirements is therefore needed.

## Decision

We deliver globally via an **edge CDN from an object store with free egress** and keep the
compute/data components small and in a US region.

| Building block | Commitment |
|---|---|
| **Scope** | This ADR covers the **image** path. **Video is hosted and delivered on a separate channel** ([ADR-0012](0012-video-transcoding-and-adaptive-delivery.md)) and is deliberately **not** served over the path below. |
| **Serving store** | **Cloudflare R2** for the globally served images (variants and served originals), behind the CDN. R2 charges **no egress** (as of 2026-06-02); ~$0.015/GB-month; S3-compatible (migratable). |
| **CDN** | **Cloudflare** in front of R2. Images are delivered worldwide from the nearest PoP. |
| **Image variants** | A few fixed variants (e.g. thumb/medium/full) are produced **on upload** and delivered as **immutable**, content-addressed objects with a long-lived `Cache-Control`. **No** transform-per-request pipeline. |
| **Provider portability** | The serving provider is **encapsulated, not hard-wired**: object storage is used **only via the S3-compatible API**, the CDN sits in front via **standard HTTP caching** (no proprietary transform/worker/stream dependency in the critical path), and the application reaches storage/CDN through a **thin abstraction** so the provider is an IaC/config detail ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)). **Backblaze B2** (also S3-compatible) is a designed-in, drop-in alternative. |
| **Authoritative master copy** | The originals' master of record is **Hetzner Object Storage** (S3-compatible, EU, in our control), independent of the serving provider, so the serving store can be re-seeded quickly — including in the event of an unexplained account suspension. |
| **API (.NET)** | Runs as a workload in the Kubernetes cluster ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)) on a Hetzner node in a **US location** (Ashburn) for API latency; image delivery runs over the global CDN anyway and is independent of the node location. |
| **PostgreSQL** | In-cluster via the **CloudNativePG** operator ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)); single primary region. Backup, replication, and recovery are binding and specified in [ADR-0013](0013-backup-and-disaster-recovery.md). |
| **Keycloak** | Runs as a workload in the cluster ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)). |
| **Scaling scope** | **No** multi-region/read-scaling replica initially; only on demonstrated demand. (A standby replica for availability/recovery is a separate concern — [ADR-0013](0013-backup-and-disaster-recovery.md).) |

**Cost target:** the image stack stays **practically traffic-independent** thanks to the
zero-egress store; the overall system (images + API + DB + Keycloak) is on the order of
**~$20–30/month**, even with growing traffic. At launch, with much data but little traffic,
**storage cost (~$3–5/month)** dominates; egress and CDN are near zero and covered by free
tiers — which makes scale-to-zero and generous free tiers genuine selection criteria. **Video
costs are separate** and not on this image stack: video is hosted on its own channel with its
own cost profile ([ADR-0012](0012-video-transcoding-and-adaptive-delivery.md)).

## Considered Options

Reference scenario (as of 2026-06-02): ~350 GB total storage including variants; egress stages
"start" (~2 TB/month) and "growth" (~20 TB/month).

- **Cloudflare R2 + Cloudflare CDN (chosen).**
  - *Pro:* egress is a **product core** and thus robustly free; image stack ~$5/month in both
    egress stages; global PoP network covers US/Japan/EU; S3-compatible (migratable); the
    worker/transform ecosystem is right next to it.
  - *Con:* concentration on one provider — a real risk given documented unexplained account
    suspensions. Mitigated by the **provider-portability** commitment above (S3-only access,
    standard HTTP, an authoritative master copy, no proprietary critical-path features), which
    keeps a swap to e.g. Backblaze B2 fast. This portability requirement is the reason the
    proprietary delivery products (Cloudflare Images/Stream) are **not** used.
- **Backblaze B2 + Cloudflare CDN.**
  - *Pro:* even cheaper storage (~$2.4/month); egress free via the Cloudflare partnership.
  - *Con:* the free egress rests on a **cancelable partnership**, not on the product core;
    somewhat more setup effort than the natively integrated R2.
- **Bunny (storage + CDN).**
  - *Pro:* technically excellent, very cheap storage.
  - *Con:* CDN egress is **usage-based** ($0.005–0.01/GB) → at 20 TB roughly $100–200/month;
    scales linearly with traffic.
- **AWS S3 + CloudFront (reference).**
  - *Pro:* market standard, very mature.
  - *Con:* egress-driven expensive: at 20 TB **~$1,500/month** — untenable for a self-paying
    operator. Serves as evidence why zero egress makes the difference.
- **Self-hosting in Germany (like the legacy application).**
  - *Con:* exactly the demonstrated weakness — high latency for the predominantly US-based
    user base.

## Rationale

Egress is the real cost lever; an object store with free egress behind a global edge CDN
solves **latency and cost at once**. Images are immutable after upload, which is why fixed,
content-addressed variants with a long-lived cache are globally faster **and** cheaper than
dynamic resize per request. With R2 the zero egress is a product core (more robust than the
cancelable B2 partnership), and the S3 compatibility keeps the storage layer migratable. The
small JSON API belongs in the US region where most users are; multi-region and read-scaling
replicas are justified only on demonstrated demand.

Concentrating delivery on one provider is a deliberate but bounded bet: because object storage
is reached only through the S3 API, the CDN only through standard HTTP, and an authoritative
master copy is kept independently, the provider is a swappable detail rather than a structural
dependency. This directly addresses the risk of an unexplained provider ban — a documented
failure mode — by making "move to Backblaze B2 (or another S3 provider) within hours" a
configuration and re-seed exercise, not a re-architecture.

## Consequences

- **Positive:** globally low latency via the CDN; very low cost largely decoupled from
  traffic; a migratable, S3-compatible storage layer; a simple cache strategy through
  immutable variants (new variant = new key, no invalidation needed).
- **Negative / Trade-offs:** delivery runs on a single provider at a time — accepted because
  the portability commitment (S3-only access, standard HTTP, own master copy, no proprietary
  critical-path features) keeps the swap cheap; API calls from EU/Asia have higher latency than
  from the USA (acceptable for a media community, since media runs over the CDN); the variant
  strategy fixes the sizes early (new sizes require subsequent generation); the master copy and
  the abstraction layer are extra moving parts to maintain.
- **To be decided later (own ADRs / detail):**
  - **Concrete compute provider** of the US API box and the **concrete Postgres variant**
    (managed vs. self-hosted).
  - **Variant set** (count/sizes) and generation site (in the API vs. a worker).
  - **Thresholds** at which read replicas / multi-region are introduced.

## References

- [ADR-0001: Purpose and Scope of the Application](0001-purpose-and-scope.md)
- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0012: Video Transcoding and Adaptive Delivery](0012-video-transcoding-and-adaptive-delivery.md)
- [ADR-0013: Backup and Disaster Recovery](0013-backup-and-disaster-recovery.md)
