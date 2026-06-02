# ADR-0011: AI Agents as a Decoupled Service

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

To rebuild the user base lost since 2023 ([ADR-0001](0001-purpose-and-scope.md)), **AI
agents** should initially boost engagement: automatically answering, reviewing, and rating
posts, plus further LLM-driven interactions. Such AI/LLM functions evolve fast and use their
own, heavily moving tooling ecosystem. It must be fixed **where** these agents sit in the
architecture and how they are attached to the core.

## Decision

AI agents run as a **separate service decoupled from the core** — **not** as a first-class
part of the backend.

1. **Agents are API clients.** They act via **the same API** that a human uses
   ([ADR-0002](0002-technology-stack.md)) — with their own **bot/service identities**
   (Keycloak service accounts). This makes agent actions attributable, rate-limitable, and
   revocable, and keeps the authoritative domain logic in **one** place (the core).
2. **Reaction via events/webhooks.** Agents react to business events by subscribing to the
   core's domain events via **webhooks**
   ([ADR-0008](0008-event-driven-architecture.md)); they do **not** access the core's
   database directly.
3. **Own language realm.** The agent service is implemented in **Python** — the most mature
   ecosystem for LLM/agent tooling. The decoupling allows this language choice without
   touching the .NET core.

The boundary between core and agents is thereby a **natural interface**: LLMs/agents are fully
decoupled from the actual system and can be swapped or switched off at any time.

## Considered Options

- **AI as a decoupled service, attached as an API client via events/webhooks (chosen).**
  - *Pro:* clean decoupling; the fast-moving LLM layer is isolated from the stable core; free
    language/tooling choice (Python); reuse of the business logic via the API; consistency is
    preserved since agents write like regular clients; switchable/replaceable without touching
    the core.
  - *Con:* an additional service and another language; a complete API and an event/webhook
    seam must exist early; synchronous "instant" AI effects cost an extra service hop
    (latency/orchestration).
- **AI as a first-class part of the .NET core.**
  - *Pro:* no service boundary; direct data access; lowest latency for inline AI.
  - *Con:* couples the fast-moving LLM ecosystem tightly to the core; .NET has the weaker
    AI/agent tooling; complicates independent scaling and the replacing/switching-off of the
    AI; contradicts the API-first stance.
- **No AI agents.**
  - *Pro:* simplest architecture.
  - *Con:* forgoes the desired lever to boost engagement in the startup phase with few users.

## Rationale

The boundary between application core and AI is a natural, clean interface: an agent is
conceptually **just another client** that uses the same API as a human. This keeps the
authoritative logic in one place, preserves consistency, makes agent actions attributable and
controllable via bot identities, and decouples the fast-moving LLM ecosystem from the stable
core — including a free language choice (Python). The few cases of seemingly tight coupling
(synchronous AI in the request path, event-driven reactions, broad read access) remain
representable via API calls, webhooks, and API read access and do not break the decoupling.

## Consequences

- **Positive:** a clearly isolated AI layer that is replaceable and switchable; best tooling
  fit through Python; reuse of the business logic; traceable, limitable agent actions.
- **Negative / Trade-offs:** an additional service and another language in operation; the API
  must be **complete** enough for agents to do everything a human can; synchronous AI effects
  carry a service hop.
- **Binding prerequisites (to be foreseen from day 1):**
  - a **complete API** as the agents' sole path of effect,
  - an **event/webhook seam** ([ADR-0008](0008-event-driven-architecture.md)),
  - **bot/service identities** in the auth model ([ADR-0002](0002-technology-stack.md)).
- **To be decided later (own ADRs):** concrete models/providers, agent framework, labeling of
  AI-generated content, guardrails/rate limits, and the cost frame of LLM usage.

## References

- [ADR-0001: Purpose and Scope of the Application](0001-purpose-and-scope.md)
- [ADR-0002: Technology Stack](0002-technology-stack.md)
- [ADR-0008: Event-Driven Architecture](0008-event-driven-architecture.md)
