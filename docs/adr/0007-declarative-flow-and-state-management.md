# ADR-0007: Declarative Flow and State Management in the Client

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

[ADR-0002](0002-technology-stack.md) clearly separates the authoritative, server-side domain
logic from the **client concerns** (presentation, navigation, offline/sync, pure UX
validation) and foresees a shared, framework-agnostic client core for them. Open is **how
user flows and UI states are structured in the client**.

The application has multi-step, state-rich flows (capturing photos and video, the
also-offline-capable composing of multi-part stories about tanks and fish, upload with
sync/transcode/error states).
Development is fully agent-supported and follows a strict test-first principle
([ADR-0003](0003-test-strategy-and-definition-of-done.md)). By experience, state scattered
imperatively across many components is hard to follow and hard to test in isolation — a risk
precisely with agent-generated code. Sought is an approach that makes flows **explicit and
checkable**.

## Decision

User flows and UI states in the client are modeled **declaratively as finite state machines
(statecharts)**, implemented with **XState**. The machines live in the shared client core and
are used jointly by Mobile and Web; the respective UI framework only binds to them.

The machines encapsulate **client flow logic only** (steps, transitions, loading/error/offline
states). The authoritative domain logic — in particular reputation/ranking computation and
moderation — stays server-side (ADR-0002); data fetches and sync operations are passed into
the machines as exchangeable effects, so the machines stay network- and framework-free.

## Considered Options

- **Declarative statecharts with XState (chosen).**
  - *Pro:* flows become explicit and representable as a data structure; states and
    transitions are pure logic, **testable in isolation and completely — before any UI
    exists**, which directly supports test-first; impossible states are structurally
    excluded; framework-agnostic and thus shareable between Mobile and Web; well suited to the
    state-rich capture/offline/upload flows.
  - *Con:* additional learning curve and dependency; overdimensioned for trivial states.
- **Imperative state management with local/hook state or a store.**
  - *Pro:* low entry barrier, very widespread, little ceremony.
  - *Con:* flow logic spreads across components and effects; hard to test in isolation;
    error-prone as complexity grows — especially with agent-generated code.
- **Pure server-side flow control, thin client.**
  - *Pro:* maximum bundling of logic in the backend.
  - *Con:* misses offline/mobile requirements and leads to chatty, sluggish UIs; client UX
    states cannot meaningfully be steered by the server.

## Rationale

Test-first is only as strong as the testability of the logic. Declarative state machines lift
user flows out of the components into a pure, fully unit-testable form — the decisive lever
with agent-generated code. They structurally exclude impossible states and are
framework-agnostic, shareable between Mobile and Web. Imperative approaches spread the logic
and undermine isolated checkability; pure server-side control does not sit well with the
mobile-first/offline claim. The clear limitation to client flow logic preserves the separation
from the authoritative server-side domain logic fixed in ADR-0002.

## Consequences

- **Positive:** explicit user flows that are testable early and in isolation; shared flow
  logic across Mobile and Web; robust state handling with impossibilities excluded; good fit
  with test-first and agent-supported development.
- **Negative / Trade-offs:** additional dependency and learning curve; risk of over-modeling
  trivial states; discipline needed so that **no** authoritative domain logic migrates into
  the machines.
- **To be decided later:** conventions for the structure, persistence, and testing of the
  machines; the interplay with the offline/sync layer of the client core.

## References

- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0003: Test Strategy and Definition of Done](0003-test-strategy-and-definition-of-done.md)
