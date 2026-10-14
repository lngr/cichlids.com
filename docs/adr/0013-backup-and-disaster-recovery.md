# ADR-0013: Backup and Disaster Recovery

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

The system's state lives in three places: **PostgreSQL** (the authoritative entity state, plus
Keycloak's backing store), the **object store** for media ([ADR-0009](0009-hosting-and-global-delivery.md)),
and **Git** for configuration and infrastructure ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)).

Of these, **database recovery is the critical risk** and, by experience, the hardest part to
get right, a backup that has never been restored is not a backup, and a long recovery-point
window means real data loss. Backup, replication, and recovery must therefore be **designed in
from the start**, not bolted on later. Media and configuration recovery must also be covered so
that the whole system can be rebuilt from a provider-independent position.

## Decision

Backup and disaster recovery are first-class and binding. The concrete tooling is the
**CloudNativePG** operator's object-store backups ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)).
The following principles hold:

1. **Defined RPO/RTO.** **RPO at most 5 minutes** (continuous write-ahead-log archiving) and
   **RTO at most 4 hours** (restore-from-backup on the single node). RTO improves automatically once a
   standby is added (only on massive growth, per ADR-0010). These targets are explicit and
   verified, not assumed.

2. **Postgres: continuous archiving + point-in-time recovery (PITR).** Backups use the
   **CloudNativePG** operator's **object-store method (Barman Cloud plugin)**: WAL is archived
   continuously and base backups are written to S3-compatible object storage
   ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)). Retention guarantees at least a
   **14-day continuous PITR window** and **monthly backups retained for twelve months**.
   The backup bucket's Compliance lock covers the longest retention period with margin.
   Bucket expiration removes objects after this period; versioned storage also requires
   noncurrent-version expiry. Locks take precedence over expiry. The plugin has no retention
   deletion policy. Daily backups and WAL share the longest lock, which increases storage cost.

   A dedicated object-storage key belongs to the primary project. A cross-project bucket
   policy grants only the object writing, reading, listing and multipart operations required
   by Barman. It grants no object deletion or bucket administration. Restores use the same
   read-capable key. No backup-project key leaves the operator.

3. **Off-provider copy (3-2-1) and confidentiality.** An independent copy lives outside the
   primary database host's provider and the media-serving provider. Backups are private and
   use TLS transport. They are unencrypted on the client side because no mature tool for
   CloudNativePG encrypts before upload. Barman Cloud supports AWS SSE-S3 and KMS;
   Hetzner supports SSE-C, which Barman does not support. Client-side encryption is required
   when a mature compatible tool exists, with durable recovery-key custody.

   Two independent safeguards protect against accidental public exposure. Static source and
   plan checks before every change reject public backup access and restrict anonymous media
   reads to serving prefixes. These checks block the CI merge gate. A scheduled external
   probe uses unauthenticated HTTPS requests and no keys to verify that backup listing,
   backup canaries and media originals are private. Any response other than access denied
   fails the probe and alerts the repository owner through configured GitHub notifications.

4. **Restores are tested on a dedicated restore-test environment.** A scheduled job regularly
   restores the **real (un-anonymized)** production backup into a **separate, strictly
   access-controlled, ephemeral** environment with **no agent/CI access**, validates it, then
   tears it down. This is the genuine restore drill (a faithful restore of real data); a backup
   path that is not exercised is treated as broken. Real PII therefore exists there only
   transiently, minimized and locked down.

5. **Non-production data per environment.** Development, staging and previews use isolated
   databases with empty initialization and synthetic seed data. They never restore production
   data and receive no production backup credentials. Production restore validation belongs in
   the dedicated operator-only restore-test environment from principle 4.

6. **Replication for availability.** A **streaming standby replica** (managed declaratively by
   CloudNativePG) provides failover/HA. This is distinct from the read-scaling replicas deferred
   in ADR-0009, and from the single-node cluster start in ADR-0010. Day one, PITR backups are
   non-negotiable; a hot standby follows as uptime requirements and cluster nodes grow.

7. **Media.** Durability rests on the serving object store **plus** the authoritative master
   copy under our own control (ADR-0009); **object versioning / lifecycle** guards against
   accidental delete or overwrite. Because media is immutable, the dominant risk is provider
   loss, which the master copy covers.

8. **Configuration and Keycloak.** Keycloak's data sits in Postgres and is covered by the
   database backup; the realm is declarative in Git, and infrastructure is reproducible via IaC
   ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)). A full rebuild is therefore: IaC apply
   then cluster bootstrap then database restore then media re-seed.

## Considered Options

- **Continuous archiving + PITR + tested restores + off-provider copy (chosen).**
  - *Pro:* small RPO; restores are proven through the operator-only restore drill; resilient to a provider
    ban; covers DB, media, and configuration coherently.
  - *Con:* a backup/restore pipeline and synthetic data generation to operate; a standby adds
    cost when introduced.
- **Provider-managed backups only.**
  - *Pro:* least effort to switch on.
  - *Con:* ties recovery to one provider (the very ban risk we guard against), often untested
    by us, and without an off-provider copy.
- **Provider VM backups (Hetzner Cloud server backups / disk snapshots).**
  - *Pro:* one click, daily, low effort.
  - *Con:* they snapshot the VM disk image (crash-consistent, daily), not the database, so no
    PITR and up to a day of loss, and they do not cover the attached database volume; they live in
    the same project as the server, reachable and deletable by the same cloud token, so they are
    neither isolated (against ADR-0017) nor immutable. The node is rebuildable cattle (cloud-init),
    so its disk needs no backup. Rejected as a backup of record; the irreplaceable data is covered
    by the object-store PITR plus the immutable off-project copy above.
- **Periodic `pg_dump` to a cron job only.**
  - *Pro:* trivially simple.
  - *Con:* no PITR, large RPO, slow restore as data grows, and easy to fail silently, exactly
    the historical pain point.

## Rationale

Database data loss is existential and historically the hardest operational problem, so the
recovery path must be engineered and continuously proven. Continuous
WAL archiving keeps the recovery-point window small; a dedicated restore-test environment that
regularly restores the **real** backup verifies recovery. Synthetic non-production data isolates
application testing from production identities. Keeping an independent copy off-provider matches
the portability posture from ADR-0009 and the documented risk of unexplained account suspensions. Media and configuration are folded in so the
entire system can be reconstituted from a provider-independent baseline.

## Consequences

- **Positive:** small, verified RPO; restores proven on a schedule; isolated synthetic
  staging data; resilience to provider loss/ban; a clear full-rebuild procedure.
- **Negative / Trade-offs:** a backup/restore pipeline, a synthetic
  data generator, **and** a separate restore-test environment to build and maintain; the
  restore-test environment holds real PII transiently (minimized, locked down, ephemeral); a
  standby replica adds cost when introduced; retention storage to pay for (cheap relative to the
  risk).
- **Operational proof:** restore drills measure RPO and RTO, verify complete WAL history and
  demonstrate the independent provider copy. The backup procedure verifies
  private access through both independent safeguards and documents encryption capabilities.
  Recovery-key custody is required for encrypted copies.

## References

- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
