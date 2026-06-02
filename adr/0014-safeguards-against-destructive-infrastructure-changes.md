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

All four layers below are **binding from day one**. The protection is **capability-based, not
approval-based**: because coding agents run under the operator's own GitHub identity, any
in-CI label or approval an agent could also grant itself — so the real control is that **no
credential able to destroy critical resources ever exists in the agent or CI environment**; that
capability is held only **out-of-band** by the operator. Machine-enforced controls (Layers 1 and
3) are the primary backstops.

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

### Layer 2 — Separated blast radius (state + credentials)
- **Two OpenTofu state tiers:** a rarely-touched **foundation/stateful** tier (node disk, volumes,
  buckets, DNS, database) and an **app/ephemeral** tier. Routine automation touches only the app
  tier; "rebuild the cluster" does not reference the data resources.
- **Least-privilege credentials:** the token agents/CI use for routine work has **no delete
  rights** on the protected/stateful resources.
- **Out-of-band break-glass credential (invariant):** the only destroy-capable credential is held
  **entirely outside** the agent and CI environment — on the operator's host, never in the repo,
  CI secrets, or the agents' devcontainer. Since agents share the operator's GitHub identity,
  this credential separation — not any approval step — is what makes a destroy impossible for an
  agent.

### Layer 3 — Pipeline gate, fail-closed (catch before execution)
- **No `destroy` in automation:** no agent or CI path wires up `tofu destroy`, and the routine
  pipeline holds no destroy-capable credential (Layer 2).
- **Automated plan-diff policy gate (the machine backstop):** **Conftest (OPA/Rego)** evaluates
  `tofu plan -json` in CI and **fails the pipeline (default deny)** on any `delete` or `replace`
  of a **critical** resource, independent of human review. *Critical* = a resource in the
  **foundation/stateful** OpenTofu state **or** one carrying the label `critical = "true"`.
- **Destroys are out-of-band, not overridden in CI:** there is **no** in-CI "allow-destroy" label
  or approval — agents run under the operator's GitHub identity and could grant it themselves.
  A legitimate destroy runs **only** via a **manually-invoked local break-glass pipeline on the
  operator's host**, using the out-of-band destroy credential (Layer 2); that script also removes
  the relevant `prevent_destroy`/delete-protection as a deliberate, logged step.
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
themselves depend on fallible humans. Layer 1 makes the destructive outcome impossible without a
deliberate, multi-step un-protection; Layer 3's fail-closed plan-diff gate catches any attempt in
the pipeline **without** relying on a reviewer; Layer 2 ensures routine automation never even
holds the rights or references to reach the data; Layer 4 guarantees recovery if every preventive
layer were somehow bypassed. Together they make accidental catastrophic loss prevented by
construction and, at worst, fully recoverable.

## Consequences

- **Positive:** accidental cluster/data destruction is prevented on multiple independent layers
  and recoverable as a last resort; the safety does not hinge on human vigilance.
- **Negative / Trade-offs:** legitimate destructive changes require a deliberate override + a
  break-glass credential (intended friction); critical resources must be consistently tagged;
  a policy gate and two credential tiers to build and operate.
- **Open items:** none — tooling (Conftest/OPA), the critical-resource convention
  (foundation-state **or** `critical=true` label), the out-of-band break-glass override, and the
  Object-Lock modes are all decided above. (Retention *durations* are set in ADR-0013.)

## References

- [ADR-0005: CI Platform and Green Gate](0005-ci-platform.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0013: Backup and Disaster Recovery](0013-backup-and-disaster-recovery.md)
