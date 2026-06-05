# ADR-0018: Environment Data Provisioning for Staging and Preview

- **Status:** Accepted
- **Date:** 2026-06-08
- **Deciders:** Alexander Langer

## Context and Problem Statement

Per-pull-request preview environments and a persistent staging environment need
realistic data, otherwise new features cannot actually be exercised against
anything resembling production. At the same time these environments must not
contain production personal data: the platform hosts real user accounts and
user-generated content, so privacy and GDPR obligations forbid replicating that
data into short-lived or shared non-production environments. The media corpus is
also large — hundreds of gigabytes to terabytes of images and video — which makes
copying it in full into every environment impractical on both time and cost.

A decision is needed on how non-production environments obtain both their database
and their media data.

[ADR-0013](0013-backup-and-disaster-recovery.md) already states the intent that
staging is fed from anonymized production data. This ADR extends that intent to
cover preview environments and the large-media problem; production backup and
disaster recovery themselves are unchanged.

## Decision

A single, anonymized **golden snapshot** is the only source of non-production data.

- The golden snapshot is produced periodically by restoring production from backup
  into an **isolated process**, masking and anonymizing it to remove or obscure all
  personal data, and publishing the result as a **versioned** snapshot. The snapshot
  consists of (a) an anonymized database image and (b) a curated, representative
  **subset** of media — never the full corpus.
- **Staging** is refreshed from the golden snapshot. It is production-shaped but free
  of personal data.
- Each **preview** environment receives its own **ephemeral database cloned from the
  golden snapshot**, so the feature under review is testable against realistic data.
  The clone is discarded when the pull request closes.
- Database cloning at scale relies on **copy-on-write** where available — storage-layer
  volume snapshots of the golden database volume, or a restore from a base backup — so
  that creating a clone is near-instant and does not physically duplicate the data.
- **Media is never copied per environment.** Non-production environments use an
  **overlay** model: reads resolve against a shared, read-only base (the golden media
  subset), while writes — new uploads made within a preview — go to a small
  per-environment ephemeral store that is consulted first and discarded with the
  environment. Per-environment media storage is therefore bounded to only what that
  environment itself writes.
- A minimal **synthetic seed** remains available as an option for the fastest smoke
  checks that do not need realistic data. The anonymized golden snapshot is the default.

## Considered Options

- **Anonymized golden snapshot + copy-on-write database clones + media overlay (chosen).**
  - *Pro:* realistic and privacy-safe; scales to a terabyte-class media corpus without
    per-environment duplication; preview provisioning is fast because clones are
    copy-on-write rather than full copies.
  - *Con:* requires building and continuously validating an anonymization pipeline;
    depends on a storage layer that supports volume snapshots; the media overlay adds
    read-path complexity.
- **Synthetic-only data.**
  - *Con:* too unrealistic to exercise real features and edge cases; data shape and
    volume diverge from production. Rejected as the primary source, retained only as an
    optional fast smoke-check seed.
- **Full production clone into each environment.**
  - *Con:* carries personal data into ephemeral, shared, and short-lived environments,
    which is unacceptable for privacy; and it would copy terabytes of media per
    environment. Rejected.
- **A managed database "branching" service offering instant copy-on-write clones.**
  - *Con:* introduces a dependency that does not fit a self-hosted platform. Rejected;
    the same instant-clone effect is approximated with storage-layer volume snapshots.

## Rationale

The chosen approach is the only one that is simultaneously realistic, privacy-safe, and
scalable to a terabyte-class media corpus without duplicating data per environment. It
achieves this by separating the **read path** from the **write path**: reads draw from a
shared, read-only base (the anonymized database snapshot and the golden media subset),
while writes land in a small ephemeral overlay that lives and dies with the environment.
Copy-on-write makes the database side of this cheap and fast, and keeping anonymization
in a single isolated process means the privacy guarantee is established once, at the
source, rather than re-litigated in every environment.

## Consequences

- **Positive:** non-production environments get realistic data with no personal data;
  the model scales to large media without per-environment copies; preview provisioning
  is fast thanks to copy-on-write clones; a single anonymization step is the only place
  privacy correctness must be enforced.
- **Negative / Trade-offs:** an anonymization pipeline must be built and kept correct,
  and it is a privacy-critical step; copy-on-write depends on a storage layer that
  supports volume snapshots; the media overlay adds complexity to the read path.
- **To be decided later:** the snapshot refresh cadence; the exact masking ruleset; the
  concrete overlay mechanism.

## References

- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0013: Backup and Disaster Recovery](0013-backup-and-disaster-recovery.md)
- [ADR-0014: Safeguards Against Destructive Infrastructure Changes](0014-safeguards-against-destructive-infrastructure-changes.md)
- [ADR-0016: Remote OpenTofu State Backend on Hetzner Object Storage](0016-remote-opentofu-state-backend.md)
- ADR-0017
