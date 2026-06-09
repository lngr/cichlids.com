# ADR-0013: Backup and Disaster Recovery

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

The system's state lives in three places: **PostgreSQL** (the authoritative entity state, plus
Keycloak's backing store), the **object store** for media ([ADR-0009](0009-hosting-and-global-delivery.md)),
and **Git** for configuration and infrastructure ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)).

Of these, **database recovery is the critical risk** and, by experience, the hardest part to
get right — a backup that has never been restored is not a backup, and a long recovery-point
window means real data loss. Backup, replication, and recovery must therefore be **designed in
from the start**, not bolted on later. Media and configuration recovery must also be covered so
that the whole system can be rebuilt from a provider-independent position.

## Decision

Backup and disaster recovery are first-class and binding. The concrete tooling is the
**CloudNativePG** operator's object-store backups ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)).
The following principles hold:

1. **Defined RPO/RTO.** **RPO ≤ 5 minutes** (continuous write-ahead-log archiving) and
   **RTO ≤ 4 hours** (restore-from-backup on the single node). RTO improves automatically once a
   standby is added (only on massive growth, per ADR-0010). These targets are explicit and
   verified, not assumed.

2. **Postgres: continuous archiving + point-in-time recovery (PITR).** Backups use the
   **CloudNativePG** operator's **object-store method (Barman Cloud plugin)**: WAL is archived
   continuously and base backups are written to S3-compatible object storage
   ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)). **Retention is tiered:** a **14-day
   continuous PITR window** (restore to any point) **plus monthly archival snapshots retained 12
   months**. (Volume snapshots may be added later only as a fast-restore accelerator; the
   authoritative, immutable, off-provider copy stays the object-store one.) Plain periodic
   `pg_dump` alone is **not** sufficient (no PITR, large RPO).

3. **Off-provider copy (3-2-1).** At least one backup copy lives in storage **independent of
   the primary database host and independent of the media-serving provider** (different
   provider/region). Backups are **encrypted at rest** with a defined **retention** policy.
   This aligns with the provider-portability stance in ADR-0009 and the risk of an unexplained
   provider ban.

4. **Restores are tested on a dedicated restore-test environment.** A scheduled job regularly
   restores the **real (un-anonymized)** production backup into a **separate, strictly
   access-controlled, ephemeral** environment — **no agent/CI access** — validates it, then
   tears it down. This is the genuine restore drill (a faithful restore of real data); a backup
   path that is not exercised is treated as broken. Real PII therefore exists there only
   transiently, minimized and locked down.

5. **Non-production data per environment.** **Development** uses **synthetic, auto-generated**
   data (never production data). **Staging** is periodically rebuilt from a production backup
   with **only user PII anonymized**, **deny-by-default** (every column in the user/identity
   scope is masked unless explicitly classified non-sensitive, so a newly added PII column is
   masked automatically). Content entities (comments, tanks, ratings, media metadata) are
   **preserved** for realism. Masking removes all real contact/identity data, so staging can
   **never** trigger real-user side effects (e.g. accidental notification emails). The restore
   *proof* lives in the restore-test environment (principle 4), not here.

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
   → cluster bootstrap → database restore → media re-seed.

## Considered Options

- **Continuous archiving + PITR + tested restores + off-provider copy (chosen).**
  - *Pro:* small RPO; restores are proven (via the staging refresh); resilient to a provider
    ban; covers DB, media, and configuration coherently.
  - *Con:* a backup/restore and anonymization pipeline to build and operate; a standby adds
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
  - *Con:* no PITR, large RPO, slow restore as data grows, and easy to fail silently — exactly
    the historical pain point.

## Rationale

Database data loss is existential and historically the hardest operational problem, so the
recovery path — not just the backup — must be engineered and continuously proven. Continuous
WAL archiving keeps the recovery-point window small; a dedicated restore-test environment that
regularly restores the **real** backup defeats the classic "backup that never restores" (the
anonymized staging refresh is for realistic non-prod data, deliberately a separate concern); and
keeping an encrypted copy off-provider matches the portability posture from ADR-0009 and the
documented risk of unexplained account suspensions. Media and configuration are folded in so the
entire system can be reconstituted from a provider-independent baseline.

## Consequences

- **Positive:** small, verified RPO; restores proven on a schedule; realistic, anonymized
  staging data; resilience to provider loss/ban; a clear full-rebuild procedure.
- **Negative / Trade-offs:** a backup/restore pipeline, a PII-anonymization step, a synthetic
  data generator, **and** a separate restore-test environment to build and maintain; the
  restore-test environment holds real PII transiently (minimized, locked down, ephemeral); a
  standby replica adds cost when introduced; retention storage to pay for (cheap relative to the
  risk).
- **Open items:** none of the backup/DR *decisions* remain open. The only schema-dependent detail
  is the **exact PII column classification** for staging masking, which is pinned when the data
  model is designed (the **deny-by-default** policy already governs it).

## References

- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
