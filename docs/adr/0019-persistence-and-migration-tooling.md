# ADR-0019: Persistence and Migration Tooling

- **Status:** Accepted
- **Date:** 2026-07-08
- **Deciders:** Alexander Langer

## Context and Problem Statement

[ADR-0002](0002-technology-stack.md) commits to PostgreSQL and a C# backend and leaves
the persistence and migration tooling open. The application needs:

1. Schema migrations as reviewable, versioned code.
2. Transactional write paths that can include an outbox record in the same database
   transaction as the domain change ([ADR-0008](0008-event-driven-architecture.md)).
3. Integration tests against a real PostgreSQL instance.
4. Bulk data paths for the one-time migration of the legacy dataset (millions of rows),
   which must be idempotent and repeatable.

## Decision

**EF Core with the Npgsql provider** is the persistence layer.

- **EF Core migrations are the single source of schema truth.** The database schema is
  changed only through migrations; no hand-applied DDL.
- **API write paths** use the DbContext with change tracking. The outbox record is
  written through the same DbContext transaction as the domain change.
- **Bulk migration paths** bypass the change tracker and use Npgsql directly (binary
  COPY, INSERT ... ON CONFLICT upserts) against the same schema. The entity model and
  the bulk paths share one schema definition, so drift between them fails visibly in
  integration tests.

## Considered Options

- **EF Core + Npgsql (chosen).** First-party stack with LTS alignment, migrations
  built in, the largest documentation and coding-agent corpus, and strong reviewability.
  ORM overhead on hot read paths is accepted until profiling demonstrates a need.
- **Dapper plus a migration runner (DbUp, FluentMigrator).** Full SQL control and
  minimal abstraction, but no change tracking for write paths, hand-written mapping
  boilerplate across a wide domain model, and a second tool needed for migrations.
- **Hybrid (EF Core for writes and schema, Dapper for hot reads).** Two data access
  idioms in one codebase without a demonstrated performance need. Stays available as a
  later, targeted optimization because it is purely additive.

## Consequences

- One model serves both the API and the legacy ETL; schema knowledge lives in one place.
- Raw SQL through Npgsql stays available as an escape hatch for measured hot spots.
- Migration history becomes part of the reviewable codebase from the first table on.

## References

- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0008: Event-Driven Architecture](0008-event-driven-architecture.md)
