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

Use **three separate cloud projects** as the capability boundary. A token in one
project cannot see or act on resources in another:

- **A primary project** holding *all* live, rebuildable infrastructure for every
  environment: compute nodes, networks, firewalls, load balancers, the per-environment
  databases, and the per-environment media stores (including the production media
  originals and the serving path). CI holds this project's *Read & Write* token and
  manages it with OpenTofu.
- **A backup project** holding *only* the immutable backups: the database
  point-in-time/WAL backups and an immutable backup copy of the media originals, on
  object storage with **Object Lock in Compliance mode**. No CI cloud token exists for
  it; the running system receives only a write-scoped object-storage credential, and
  Compliance-mode immutability prevents deletion of backup objects even by that
  credential.
- **A state project** holding *only* the OpenTofu remote-state storage (versioned,
  without Object Lock, consistent with
  [ADR-0016](0016-remote-opentofu-state-backend.md)). No CI cloud token exists for it;
  CI receives only object-storage credentials to read and write state objects.

The principle: since tokens cannot be permission-scoped, isolation is **by project**.
Everything in the primary project is "cattle" — rebuildable, with its data recoverable
from the immutable backups. The irreplaceable assets — the immutable backups, the
backup copy of the media originals, and the state — live in projects whose cloud token
never enters automation.

This refines the blast-radius intent of ADR-0014: the unachievable "token without
delete rights" is replaced by "a routine token confined to a project that holds nothing
irreplaceable," while ADR-0014's other layers remain in force — undeletable-by-
construction in the OpenTofu path, the fail-closed plan-diff gate, and assume-breach
immutable backups.

## Considered Options

- **Single project with token-level least privilege.**
  - *Con:* not achievable — the provider's tokens are only *Read* or *Read & Write*,
    with no "may not delete" level. Rejected as the founding constraint of this ADR.
- **Two projects split into "data" vs "platform".**
  - *Con:* block-storage volumes attach only to servers within the same project, so the
    live database node and its volume cannot be separated across the boundary. This
    forces awkward placement and conflicts with keeping all live infrastructure
    together. Rejected.
- **Three projects (live / backup / state).** **Chosen.**
  - *Pro:* an enforceable capability boundary that does not depend on token permission
    granularity; the automation token is confined to rebuildable infrastructure; the
    irreplaceable data sits where no automation token can reach it.
  - *Con:* three projects and several credentials to administer; the backup and state
    projects are bootstrapped out-of-band.
- **Separate cloud accounts/organizations** (as larger providers do with dedicated
  backup and log-archive accounts).
  - This is the same isolation pattern at a different provider; the three-project split
    is its equivalent here.

The volume-attach constraint is the decisive reason to keep all live compute and
storage together in the primary project: durability comes from the immutable backups,
not from isolating the live volume across a project boundary it cannot legally cross.

## Rationale

The catastrophic outcome becomes **bounded by construction**. The automation token
simply cannot reach the irreplaceable data, regardless of what scripts run or how a
token is misused, because that data lives in projects the token cannot see. Whatever
happens to the primary project, recovery is always possible from the immutable backups.
This is a stronger guarantee than any permission-scoping the provider can offer, and it
holds even against arbitrary raw-API calls rather than only the OpenTofu path.

## Consequences

- **Positive:** a capability boundary that is actually enforceable on this provider;
  automation is free to manage all live infrastructure declaratively; the irreplaceable
  data is out of automation's reach and recoverable from immutable backups.
- **Negative / Trade-offs:** three projects and several credentials to administer; the
  backup and state projects are bootstrapped out-of-band by the operator; backups
  require cross-project object-storage wiring.
- **Residual exposures, stated honestly:**
  - The DNS API is account-level rather than project-scoped, so a DNS credential held by
    automation can change records. Mitigated by using the most-scoped DNS credential
    available; the zone itself is reconstructible and delegation lives at the registrar.
  - The object-storage credential automation holds for state can delete state objects.
    Mitigated by versioning, which allows rollback.
  - Backup objects remain protected by Compliance-mode immutability regardless of the
    credential that touches them.
- **To be decided later:** the exact granularity of object-storage credential scoping
  available on this provider.

## References

- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0013: Backup and Disaster Recovery](0013-backup-and-disaster-recovery.md)
- [ADR-0014: Safeguards Against Destructive Infrastructure Changes](0014-safeguards-against-destructive-infrastructure-changes.md)
- [ADR-0016: Remote OpenTofu State Backend on Hetzner Object Storage](0016-remote-opentofu-state-backend.md)
