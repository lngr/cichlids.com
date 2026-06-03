# ADR-0008: Event-Driven Architecture

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

The community described in [ADR-0001](0001-purpose-and-scope.md) produces business events to
which many downstream reactions are attached: a new image or a new story triggers feed update,
reputation recomputation, notifications, and — see
[ADR-0011](0011-ai-agents-as-decoupled-service.md) — AI-driven reactions. If these reactions
were executed synchronously in the write path, it would couple the components tightly, make
writes slow, and prevent independent scaling.

Since the authoritative logic lives server-side ([ADR-0002](0002-technology-stack.md)) and
external consumers (especially the decoupled AI agents) must react to events reliably, a
binding decision on the architecture of event processing is needed.

## Decision

The system is **thoroughly event-driven**. Business state changes are modeled as **domain
events**; downstream effects occur **asynchronously** through consumers of these events. Three
building blocks are binding:

1. **Domain events** as a first-class concept: every relevant state change (e.g. "image
   uploaded", "story published", "comment created", "rating given") produces an event with a
   stable schema and versioning.

2. **Transactional outbox:** events are written into an outbox **in the same database
   transaction** as the business state change. A separate relay then publishes them reliably
   (at-least-once) to the broker. This rules out a state change without its event, or vice
   versa.

3. **Webhooks** as the delivery path to **external/decoupled consumers** — first and foremost
   the AI agent service ([ADR-0011](0011-ai-agents-as-decoupled-service.md)). External
   consumers react via webhooks/subscribed events, not by direct access to the core's
   database.

Consumers must be **idempotent** (at-least-once delivery).

This event-driven backbone is the whole decision here. It **opens up** the options below.
They are **deliberately not decided in this ADR** — persistence and processing styles are chosen
later, per aggregate, when the data model is designed:

- **Per-aggregate persistence freedom.** An aggregate can be modeled as classic **CRUD**, as an
  **event-sourced** aggregate (its state being its ordered event history), or surfaced only
  through **stream-processed materialized projections** — all three remain available.
- **Derived read models** (reputation, feed, discovery/"interesting") can be built as
  projections / stream processors over the event log, kept eventually consistent.
- **Replay, audit, and temporal queries** become possible wherever the event history is retained.
- **Order handling is a choice.** Order-independent aggregation (counts, sums, votes) can use
  **commutative** operations that shard and merge in parallel; where order matters it can be
  preserved **per partition key** or via an **event-sourced aggregate**.
- **Independent scaling** of producers and downstream consumers (feed, reputation,
  notifications, AI agents).

The concrete **message broker / queue** is likewise not decided here (see "Consequences").

## Considered Options

- **Synchronous, inline processing (no event backbone).**
  - *Pro:* simplest implementation, no broker infrastructure.
  - *Con:* tight coupling, slow write paths, no independent scaling; external reactions (AI)
    would have to be built into the core — contradicts ADR-0011.
- **Event-driven with transactional outbox + webhooks (chosen).**
  - *Pro:* decouples the write path from downstream effects; components scale independently;
    reliable publishing without distributed transactions; a natural, clean seam for
    external/decoupled consumers (AI agents); leaves persistence and processing styles
    (CRUD, event sourcing, stream-processed projections) open to choose per aggregate.
  - *Con:* more moving parts (outbox relay, broker, idempotent consumers); eventual
    consistency requires care in design and tests.

> Persistence/processing styles (CRUD, event sourcing, stream processing) are **not** weighed
> here — they are downstream choices this backbone keeps open, to be made per aggregate when the
> data model is designed.

## Rationale

An event-driven architecture is, for this product, not optional comfort but a precondition for
**scaling and decoupling**: the write path stays lean while feed, reputation, notifications,
and AI reactions work independently and asynchronously. The transactional outbox solves the
core problem of reliable publishing without distributed transactions; webhooks form the clean
boundary to external consumers and make the decoupling of the AI agents required in ADR-0011
viable in the first place. Committing to the backbone now — and **nothing more** — secures
scaling and decoupling while deliberately keeping the persistence and processing styles open:
CRUD, event sourcing, and stream-processed projections all remain on the table and are chosen
per aggregate later, when the data model is designed.

## Consequences

- **Positive:** loosely coupled, independently scalable components; reliable event publishing;
  a clear, subscribable boundary for external services and AI agents; good extensibility with
  new reactions without touching the write path.
- **Negative / Trade-offs:** additional infrastructure and operating complexity (broker,
  outbox relay); eventual consistency and idempotency must be designed and tested deliberately
  ([ADR-0003](0003-test-strategy-and-definition-of-done.md)).
- **To be decided later (own ADRs):**
  - **Concrete broker / queue.** Start pragmatically with a **Postgres-backed** queue/log
    (Postgres scales far enough for a long time and avoids extra infrastructure cost, in line
    with the cost stance from [ADR-0009](0009-hosting-and-global-delivery.md)); move to a
    **partitioned, replayable log** (Kafka/Redpanda-style) only under demonstrated scaling
    pressure. Partitioning follows the key whose order must be preserved (see the decision).
  - **Per-aggregate persistence and processing style** (CRUD, event sourcing, or
    stream-processed projection) — decided per aggregate when the data model is designed.
  - **Event-schema management and versioning.**
  - **Webhook security** (signatures, delivery guarantees, retries, dead-letter).

## References

- [ADR-0001: Purpose and Scope of the Application](0001-purpose-and-scope.md)
- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0011: AI Agents as a Decoupled Service](0011-ai-agents-as-decoupled-service.md)
