# ADR-0026: Legacy Import Runs Once Against an Empty Target

- **Status:** Accepted
- **Date:** 2026-10-12
- **Deciders:** Alexander Langer

## Context and Problem Statement

The legacy dataset moves into the new schema through a bulk import
([ADR-0019](0019-persistence-and-migration-tooling.md)). An import that may run against a database
which already holds data has to reconcile every row it finds: rows from an earlier run, rows
members created since, rows an earlier version of the import shaped differently. Each of those
cases is a branch that is hard to test and easy to get wrong, and a mistake in one of them
corrupts production data silently. A run that stops halfway leaves a target that is neither the
old state nor the new one.

Production receives the import exactly once, at go-live. Development and staging need to repeat
it often, to test the import and to rehearse the go-live under the same conditions.

## Decision

- **The import writes only into an empty target.** Before its first write it checks that every
  schema migration is applied and that no table of the application schemas holds a row, and it
  stops with the reason otherwise. The check holds a database lock for the whole run, so two runs
  started together cannot both pass.
- **The import is atomic.** All of its writes happen in one database transaction. A failed run
  leaves the target empty and can start over. Verification against the legacy source runs after
  the commit and fails the run on a mismatch without undoing it.
- **A reset exists for Development and Staging only.** It drops and rebuilds the application
  schemas, deletes the identity realm's accounts except those with administrative rights, and
  closes self-registration. Every other environment, an unnamed one included, is refused. The
  reset leaves the object store alone, so media objects stay across resets.
- **Production runs the import exactly once.** Corrections after go-live are schema or data
  migrations ([ADR-0019](0019-persistence-and-migration-tooling.md)).
- **After go-live staging is refreshed from production data** as
  [ADR-0018](0018-environment-data-provisioning.md) describes; the reset and the import serve
  staging until then.
- Single import steps outside the guarded run exist for debugging in Development and are refused
  elsewhere.

## Considered Options

- **Empty target, atomic run, reset outside production (chosen).**
  - *Pro:* the import has one starting state that tests can cover completely; every
    dev and staging run rehearses the production run; a failed production run is simply retried.
  - *Con:* one long transaction grows the write-ahead log during the run; every dev and staging
    run starts from scratch and takes the full import time.
- **Idempotent, repeatable import over existing data.**
  - *Pro:* a rerun after a partial failure continues where it stopped.
  - *Con:* every write path needs conflict handling for states that never occur in the single
    production run; a run against a database with member-created data can overwrite or orphan
    it; the combinations are not testable exhaustively.
- **A dry-run mode that rolls back.**
  - *Con:* a second run mode to keep correct. A real run on a resettable database shows the same
    outcome.

## Rationale

The import is a one-time operation in production, so it pays to design it for exactly one
starting state and make that state cheap to reach everywhere else. With the guard, a target in
the wrong state stops the run before any write. A partial failure rolls back as a whole and the
run is retried. Development and Staging reach the empty state through the reset, which refuses
every environment that is not named explicitly and so stays away from production even when the
environment setting is missing.

## Consequences

- **Positive:** The import has one well-defined precondition. Dev and staging runs are
  reproducible and rehearse go-live. A run that fails before its commit leaves the database
  empty; what it wrote to the object store (the bucket, its policy, forum attachments) can
  remain there. A verification mismatch after the commit keeps the imported rows and fails the
  run with exit code 1.
- **Negative / Trade-offs:** Data created by hand in a dev or staging database is lost on the
  next reset. The reset needs the database owner role. A long single transaction needs enough
  disk for its write-ahead log.
- **To be decided later:** How the reset runs as a staging job in the deployed cluster.

## References

- [ADR-0018](0018-environment-data-provisioning.md)
- [ADR-0019](0019-persistence-and-migration-tooling.md)
- [ADR-0022](0022-legacy-account-import-into-keycloak.md)
