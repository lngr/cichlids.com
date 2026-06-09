# ADR-0014: Safeguards Against Destructive Infrastructure Changes

- **Status:** Accepted
- **Date:** 2026-06-03
- **Deciders:** Alexander Langer

## Context and Problem Statement

Under strict, reconciling GitOps ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)),
automation — CI pipelines and coding agents — can change infrastructure. An automated agent can,
during debugging, execute a **catastrophic destructive operation**: `tofu destroy`, replacing or
recreating the whole cluster, or deleting the database, volumes, or object-storage buckets. This
has occurred in practice.

Human review is **not** a sufficient safeguard: a single critical destructive line can — and
does — slip through review unnoticed. Protection must therefore be **machine-enforced and
layered (defense in depth)**, designed so that the dangerous outcome is prevented *by
construction* and, failing that, fully recoverable — never dependent on a reviewer catching the
line.

## Decision

All four layers below are **binding from day one** and machine-enforced rather than
approval-based: because coding agents run under the operator's own GitHub identity, any in-CI
label or approval an agent could also grant itself. The catastrophic outcome is bounded by
construction: the **irreplaceable backups are isolated** in a project no automation token can
reach (ADR-0017) and are immutable, so any destruction of live infrastructure is **recoverable**.
Within the live project, accidents are prevented by Layer 1 and caught by the plan-diff gate
(Layer 3); recovery is guaranteed by Layer 4.

### Layer 1 — Critical resources are undeletable by default (by construction)
- **OpenTofu `lifecycle { prevent_destroy = true }`** on every critical resource (cluster node
  and its disk, DB volume, master and backup buckets, DNS zone) → `destroy`/replace **fails**.
- **Hetzner delete-protection** flag on servers and volumes → the provider API refuses deletion
  until protection is *explicitly* removed.
- **Object storage immutability (per bucket):** the **backup** bucket uses **Object Lock in
  Compliance mode** (immutable for the retention window — not even root can delete or shorten
  it); the **media-master** bucket uses **versioning + Object Lock in Governance mode** (routine
  credentials cannot delete/overwrite, but a privileged break-glass action can — so **GDPR
  erasure remains possible**). If the chosen provider lacks Object Lock, the compliance-locked
  copy lives on one that does (e.g. Backblaze B2 / S3). Retention durations are set in
  [ADR-0013](0013-backup-and-disaster-recovery.md).
- **Argo CD `Prune=false` + finalizers** on stateful resources → reconciliation never deletes the
  CloudNativePG cluster, PVCs, or namespaces, even if a manifest disappears.

### Layer 2 — Separated blast radius (the irreplaceable data is isolated)
- **The immutable backups live in a separate project with no cloud token** (ADR-0017): the
  database PITR/WAL backups and the off-master copy of the media originals. No automation
  credential can see or delete them, and Compliance-mode Object Lock blocks deletion even with the
  operator's object-storage credential. Everything in the live `cichlids` project is rebuildable
  cattle, recoverable from these backups.
- **The provider has no "may-not-delete" token level** (Read or Read & Write only), so
  least-privilege cannot be enforced through the token; the enforceable boundary is the project
  (ADR-0017). The live infrastructure is therefore guarded against accidents by Layer 1, not by
  withholding it from automation.

### Layer 3 — Fail-closed plan-diff gate (catch before execution)
- **Automated plan-diff policy gate:** when CI applies the live project, **Conftest (OPA/Rego)**
  evaluates the `tofu plan -json` and **fails the pipeline (default deny)** on any `delete` or
  `replace` of a **critical** resource, while allowing in-place updates (so CI can resize a volume
  but never destroy it), independent of human review. *Critical* = a protected resource type **or**
  one carrying the label `critical = "true"`. The policy and its unit tests live in `policy/`; the
  gate wires into the CI-apply pipeline when that tier is built.
- **Deliberate destroys go through Git, not an in-CI override:** there is **no** in-CI
  "allow-destroy" label or approval (an agent could grant it itself). A legitimate destroy of a
  critical resource is a deliberate, reviewed change that removes the resource's
  `prevent_destroy`/delete-protection; the immutable backups make even a mistaken one recoverable.
- Branch protection on `main` (PR + green gate + review) per [ADR-0005](0005-ci-platform.md).

### Layer 4 — Assume-breach recovery (last line)
- **Immutable, off-provider, tested backups** ([ADR-0013](0013-backup-and-disaster-recovery.md)),
  stored with credentials that agents/CI do **not** hold → even a successful destroy is fully
  recoverable.

## Considered Options

- **Full machine-enforced defense in depth (all four layers) — chosen.**
  - *Pro:* the catastrophic outcome is prevented by construction *and* by a fail-closed pipeline
    gate *and* recoverable if all else fails; protection does not depend on a reviewer spotting a
    line.
  - *Con:* friction for legitimate destroys; discipline to tag critical resources; two credential
    tiers and a policy gate to build and maintain.
- **Rely on human review / approval only.**
  - *Con:* demonstrably insufficient — critical destructive lines slip through review; no backstop
    when they do. Rejected.
- **In-CI override via an "allow-destroy" label / approval.**
  - *Con:* coding agents run under the operator's own GitHub identity, so an agent could grant
    the label/approval itself. Rejected in favor of an **out-of-band, capability-based** override
    (a local break-glass pipeline with a credential never present in the agent/CI environment).
- **A single mechanism (e.g. only `prevent_destroy`).**
  - *Con:* one control has gaps (e.g. resources not yet tagged, or a path that bypasses Tofu); a
    single layer is not enough for an existential risk. Rejected in favor of layering.

## Rationale

The risk is existential and the triggering actor is fallible automation, so the controls must not
themselves depend on fallible humans. Layer 1 makes destruction of a live resource impossible
without a deliberate, multi-step un-protection; Layer 3's fail-closed plan-diff gate catches any
such attempt in the pipeline **without** relying on a reviewer; Layer 2 keeps the irreplaceable
backups in a project no automation token can reach; Layer 4 guarantees recovery if every
preventive layer were somehow bypassed. Together they make accidental catastrophic loss prevented
by construction and, at worst, fully recoverable.

## Consequences

- **Positive:** accidental cluster/data destruction is prevented on multiple independent layers
  and recoverable as a last resort; the safety does not hinge on human vigilance.
- **Negative / Trade-offs:** a legitimate destroy of a critical resource requires a deliberate,
  reviewed change that removes its protection (intended friction); critical resources must be
  consistently tagged; a policy gate to build and operate.
- **Open items:** none. The tooling (Conftest/OPA), the critical-resource convention (protected
  type **or** `critical=true` label), the project isolation of the backups (ADR-0017), and the
  Object-Lock modes are all decided above. (Retention *durations* are set in ADR-0013.)

## References

- [ADR-0005: CI Platform and Green Gate](0005-ci-platform.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0013: Backup and Disaster Recovery](0013-backup-and-disaster-recovery.md)
- [ADR-0016: Remote OpenTofu State Backend on Hetzner Object Storage](0016-remote-opentofu-state-backend.md)
- [ADR-0017: Hetzner Project Separation for Capability-Based Blast Radius](0017-hetzner-project-separation.md)
