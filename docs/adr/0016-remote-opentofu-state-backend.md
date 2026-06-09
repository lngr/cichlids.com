# ADR-0016: Remote OpenTofu State Backend on Hetzner Object Storage

- **Status:** Accepted
- **Date:** 2026-06-05
- **Deciders:** Alexander Langer

## Context and Problem Statement

[ADR-0017](0017-hetzner-project-separation.md) separates the infrastructure across two projects
(the cichlids platform and app tiers, and the immutable backups), and
[ADR-0010](0010-gitops-and-infrastructure-as-code.md) makes OpenTofu the provisioner that
bootstraps the platform. Neither decides **where the OpenTofu state itself lives**.

This is not a detail. Without an explicit backend, OpenTofu keeps state in a local file. For the
platform tier that file would record the only management handle to the hard-to-recreate
resources (compute node, database volume, media-master bucket, DNS zone). Losing it
— a discarded container, a wiped workstation — orphans those resources: they keep existing and
billing but can no longer be planned, changed, or even deliberately destroyed through OpenTofu,
which directly undermines the ADR-0014 safeguards. The app tier additionally runs from CI, where
a local file is not an option at all and concurrent runs must not corrupt state.

A durable, lockable, machine-reachable state backend is therefore required before any real
`apply`.

## Decision

Both OpenTofu tiers (platform and backup) use a **remote `s3` backend on Hetzner Object Storage**,
with the following properties:

- **A dedicated state bucket per tier, in that tier's own project** (ADR-0017): the platform
  state in `cichlids`, the backup state in `cichlids-backup`. Each is separate from the buckets
  that tier manages (media-master, the backup buckets), so the state never lives inside a resource
  its own state creates. Each state bucket is created **once, out-of-band** before the first
  `init` and is not managed by either tier. Keeping the backup state in `cichlids-backup` keeps it,
  like the backups, out of CI's reach.
- **Versioning enabled** on the state bucket, so a corrupted or truncated write can be rolled
  back to a previous object version.
- **Native state locking** via the backend's S3 lock-file mechanism (`use_lockfile`), which uses
  a lock object in the same bucket and needs no separate lock database.
- **No Object Lock** on the state bucket. State and its lock object are mutable by design; an
  Object-Lock retention would forbid deleting the lock object and produce stuck locks. Integrity
  here comes from versioning, private access, and at-rest encryption — *not* from immutability.
  (Object Lock remains in force only on the media-master and backup buckets, per ADR-0014.)
- **Native OpenTofu state encryption** (the `encryption` block) as defense in depth, because
  state contains secrets in cleartext — provider tokens, S3 keys, and any generated passwords.
  The key material is held **out-of-band by the operator** and is never stored in the repository
  or in CI.

The state bucket is private throughout. Access keys for it are supplied at `init` time from the
environment, never committed.

## Considered Options

- **Remote `s3` backend on Hetzner Object Storage (chosen).**
  - *Pro:* durable and independent of any one machine; native locking and atomic writes make it
    safe for CI-driven runs and concurrent access; versioning allows rollback; one mechanism
    serves both tiers; co-located with the rest of the platform.
  - *Con:* requires a one-time out-of-band bucket bootstrap (chicken-and-egg); key and lock
    handling to get right.
- **Local state file (the implicit default).**
  - *Con:* a single point of loss that orphans the protected resources; unusable from CI; no
    locking. Rejected — it is exactly the gap this ADR closes.
- **State on a synced shared folder (e.g. Dropbox).**
  - *Con:* background sync produces conflict copies and partial syncs that silently corrupt live
    state, and there is no locking. Rejected.
- **State in a separate Git repository.**
  - *Con:* Git is not built for large, frequently-rewritten binary-ish state, offers no locking,
    and invites merge conflicts on a file that must never be merged. Rejected in favor of the S3
    backend.

## Rationale

The chosen option is the only one that is simultaneously durable, lockable, machine-reachable,
and rollback-capable — the properties the platform's protected resources and the CI-driven app
tier both require. A dedicated, unmanaged bucket avoids the dependency cycle of storing state
inside resources that the same state creates. Deliberately omitting Object Lock on the state
bucket avoids a self-inflicted denial of service on the lock object, while versioning, private
access, and encryption still protect integrity and confidentiality. Native encryption keeps the
cleartext secrets in state from being readable at rest, with keys never leaving the operator.

## Consequences

- **Positive:** state is durable, lockable, versioned, and encrypted; safe for concurrent and
  CI-driven runs; one backend pattern for both tiers; the ADR-0014 safeguards rest on a
  management handle that cannot be lost with a single machine.
- **Negative / Trade-offs:** a one-time operator bootstrap of the state bucket precedes the first
  `init`; the encryption key and the state-bucket credentials are additional out-of-band secrets
  to manage.
- **State placement:** the platform state lives in `cichlids` (reachable by the same credential CI
  uses to manage that project) and the backup state in `cichlids-backup` (operator-only). The
  backup state thus stays out of CI's reach, while the platform state, being recoverable cattle,
  does not need its own project (ADR-0017). The app tier, when built, shares the platform state
  project under its own state key.

## References

- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0014: Safeguards Against Destructive Infrastructure Changes](0014-safeguards-against-destructive-infrastructure-changes.md)
