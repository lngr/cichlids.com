# ADR-0017: Hetzner Project Separation for Capability-Based Blast Radius

- **Status:** Accepted
- **Date:** 2026-06-08
- **Deciders:** Alexander Langer

## Context and Problem Statement

Under reconciling GitOps, CI holds cloud credentials and will, over time, manage a
growing set of infrastructure with OpenTofu — additional virtual machines, load
balancers, firewalls, and networks. CI therefore needs a write-capable cloud token.

The cloud provider's API tokens are scoped to a single project and offer only two
levels — *Read* or *Read & Write* — with no resource-level "may not delete"
permission. A routine write-capable token can therefore, through the raw cloud API,
disable a resource's delete-protection and then delete it, bypassing OpenTofu's
`prevent_destroy` lifecycle and the plan-diff policy gate: those guards only constrain
the OpenTofu execution path, not arbitrary API calls made with the token.

This means the "routine least-privilege token without delete rights" assumed as a
blast-radius layer in [ADR-0014](0014-safeguards-against-destructive-infrastructure-changes.md)
is **not achievable on this provider**. A capability boundary is needed that does not
depend on token-level permission granularity.

## Decision

Use **two separate cloud projects** as the capability boundary. A token in one project cannot see
or act on resources in another:

- **`cichlids`** holds *all* live infrastructure for every environment: compute nodes, networks,
  firewalls, load balancers, the per-environment databases, the media stores (production originals
  and the serving path), the DNS zone (project-scoped on the Cloud API, so the project token also
  authorises it), and this project's own OpenTofu state. CI holds this project's *Read & Write*
  token and manages everything here via OpenTofu under GitOps. Everything in `cichlids` is
  "cattle": rebuildable, with its data recoverable from the immutable backups, so automation
  manages it freely. Accidental destruction is guarded by `prevent_destroy` and delete-protection
  (ADR-0014 Layer 1), not by withholding the project from automation.
- **`cichlids-backup`** holds *only* the irreplaceable assets: the database point-in-time/WAL
  backups and an immutable backup copy of the media originals, on object storage with **Object
  Lock in Compliance mode**, plus this tier's own OpenTofu state. It has **no cloud token**; it is
  reached only by an operator-held write-scoped object-storage credential that never enters CI,
  and Compliance-mode immutability prevents deleting a backup object even with that credential.

The principle: only the **irreplaceable backups** need isolation. Each tier's OpenTofu state lives
with its tier (the `cichlids` state in `cichlids`, the backup state in `cichlids-backup`), so a
credential reaches only its own project's state and the backup state stays out of CI's reach. The
state in `cichlids` is itself cattle (a loss is re-imported; the resources persist and the data is
in the backups), so it does not warrant a project of its own.

This refines the blast-radius intent of ADR-0014: the unachievable "token without delete rights"
is replaced by confining the only irreplaceable asset to a project no automation token can reach,
while ADR-0014's other layers remain in force (undeletable-by-construction in the OpenTofu path
and assume-breach immutable backups).

## Considered Options

- **Single project with token-level least privilege.**
  - *Con:* not achievable — the provider's tokens are only *Read* or *Read & Write*,
    with no "may not delete" level. Rejected as the founding constraint of this ADR.
- **Two projects split into "data" vs "platform".**
  - *Con:* block-storage volumes attach only to servers within the same project, so the
    live database node and its volume cannot be separated across the boundary. This
    forces awkward placement and conflicts with keeping all live infrastructure
    together. Rejected.
- **Separate state projects (isolating all OpenTofu state from CI).**
  - *Con:* over-isolation. The CI-managed state is itself recoverable cattle (a loss is
    re-imported; the resources persist and the data is in the immutable backups), so it does not
    need a project of its own. Only the backups and the operator's backup state require isolation.
- **Two projects (`cichlids`: all live infrastructure plus its state; `cichlids-backup`: the
  immutable backups plus the backup state). Chosen.**
  - *Pro:* an enforceable capability boundary on the only thing that is irreplaceable; automation
    manages all live infrastructure declaratively; the backups sit where no automation token can
    reach them; the fewest projects that still bound the catastrophic outcome.
  - *Con:* the backup project is bootstrapped out-of-band by the operator; backups require
    cross-project object-storage wiring.
- **Separate cloud accounts/organizations** (as larger providers do with dedicated backup and
  log-archive accounts).
  - This is the same isolation pattern at a different provider; the two-project split is its
    equivalent here.

The volume-attach constraint is why all live compute and storage stay together in `cichlids`:
durability comes from the immutable backups, not from isolating the live volume across a project
boundary it cannot legally cross.

## Rationale

The catastrophic outcome becomes **bounded by construction**. The automation token
simply cannot reach the irreplaceable data, regardless of what scripts run or how a
token is misused, because that data lives in a project the token cannot see. Whatever
happens to `cichlids`, recovery is always possible from the immutable backups.
This is a stronger guarantee than any permission-scoping the provider can offer, and it
holds even against arbitrary raw-API calls rather than only the OpenTofu path.

## Consequences

- **Positive:** a capability boundary on the only irreplaceable asset; automation manages all
  live infrastructure declaratively; the backups are out of automation's reach and recoverable.
- **Negative / Trade-offs:** the backup project is bootstrapped out-of-band by the operator;
  backups require cross-project object-storage wiring.
- **Residual exposures:**
  - A state-writing credential can overwrite or delete the state in its own project (the provider
    offers no write-without-delete, and Object Lock is incompatible with mutable state). Versioning
    makes this recoverable; the `cichlids` state is cattle, and the backup state is operator-only.
  - Backup objects remain protected by Compliance-mode immutability regardless of the credential
    that touches them.

## References

- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0013: Backup and Disaster Recovery](0013-backup-and-disaster-recovery.md)
- [ADR-0014: Safeguards Against Destructive Infrastructure Changes](0014-safeguards-against-destructive-infrastructure-changes.md)
- [ADR-0016: Remote OpenTofu State Backend on Hetzner Object Storage](0016-remote-opentofu-state-backend.md)
