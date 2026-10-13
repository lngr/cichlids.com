# ADR-0024: Webhook Delivery Boundary and Redelivery

- **Status:** Accepted
- **Date:** 2026-10-09
- **Deciders:** Alexander Langer

## Context and Problem Statement

The core publishes domain events through a transactional outbox and delivers them to
subscribers as webhooks ([ADR-0008](0008-event-driven-architecture.md)). Subscribers are of
two kinds. Internal services run inside the platform's own network, first of all the AI
agent service ([ADR-0011](0011-ai-agents-as-decoupled-service.md)). External subscribers are
services outside the platform, registered by URL.

A webhook is an HTTP request the server sends to a URL it was given. Without a boundary, a
registered URL can point at anything the server itself can reach: databases, admin
consoles, the cloud provider's metadata endpoint. This is server-side request forgery. A
redirect from a harmless external address leads to the same place. Blocking all private
addresses closes that hole, but it also locks out the internal subscribers the
architecture relies on.

Delivery also has to survive subscriber outages. A subscriber that is down for a weekend,
or during its own deployment, must receive the events published in the meantime once it is back,
and delivery to a failing subscriber must never stop the core.

The question is which targets the core delivers to directly, how external targets are
reached, and how long the core keeps trying.

## Decision

**Delivery boundary.**

- The core delivers webhooks only to hosts on an allowlist. The allowlist is
  configuration per environment and lives in Git ([ADR-0010](0010-gitops-and-infrastructure-as-code.md)),
  so every new target goes through review. A registration for any other host is rejected.
- Only `http` and `https` targets are accepted. Redirects are not followed.
- Arbitrary external targets are reached only through a separate **webhook forwarder**
  service. The core delivers to the forwarder, which is on the allowlist; the forwarder
  delivers to the external URLs. A network policy allows the forwarder outbound traffic to
  the internet and denies it any traffic into the platform's internal network.

**Redelivery.**

- A failed delivery is retried with a delay that doubles from attempt to attempt and is
  capped at one hour. The delay counts from the previous attempt.
- An event is given up only when it is older than a configurable period, seven days by
  default. Until then, it is retried at least hourly.
- An administrator can trigger the redelivery of given-up events. A redelivery starts a new
  period: the event is retried until the configured period has passed since the redelivery.
- Each event keeps a stable id across redeliveries, so subscribers can discard duplicates.
- A failure while delivering one event counts as a failed attempt of that event. It never
  stops the delivery of other events or the core itself.

## Considered Options

- **Allowlist in the core, forwarder for external targets (chosen).**
  - *Pro:* Internal subscribers are allowed explicitly and nothing else is reachable from
    the core. Even a compromised administrator account cannot add a new internal target,
    because the allowlist changes only through Git. The forwarder isolates the internal
    network by network policy, which holds independent of application code. Slow or dead
    external subscribers load only the forwarder, which scales independently of the core.
  - *Con:* An additional service to run, and an extra hop for external deliveries.
- **Block private, loopback and link-local addresses at connect time.**
  - *Pro:* The common protection for services that accept arbitrary webhook URLs; no
    extra service.
  - *Con:* Locks out the internal subscribers, the AI agent service in particular. Address
    checks in application code are easy to get subtly wrong (DNS rebinding, IPv6 forms).
- **Accept the risk, since only administrators register webhooks; disable redirects.**
  - *Pro:* No effort.
  - *Con:* A single compromised administrator account reaches every internal service.
- **Give up after a fixed number of attempts.**
  - *Pro:* Simple to reason about.
  - *Con:* The covered time span depends on the backoff curve; with a doubling delay it
    stays short and a weekend outage loses events.

## Rationale

The allowlist and the forwarder separate internal from external delivery at the network
layer. The core talks only to known targets, and the only component that talks to
arbitrary URLs has no route into the platform. This keeps the internal subscribers the
architecture is built on, which a pure address block would forbid. Expressing the
give-up rule as a time span makes the guarantee explicit: an outage shorter than the span
loses nothing, regardless of how many attempts fall into it. Seven days covers a long
weekend or a holiday week of the operator, and an administrator can recover anything older.

## Consequences

- **Positive:** No server-side request forgery into internal services through webhooks.
  Internal subscribers work without exceptions in code. Subscriber outages up to the
  configured span lose no events. A failing delivery never takes the core down.
- **Negative / Trade-offs:** The forwarder is one more service with its own deployment,
  monitoring and network policy. Events can reach a subscriber up to seven days late and
  more than once, so subscribers must be idempotent. Outbox rows of failing subscribers
  stay for the configured span.
- **To be decided later:** How external subscribers register with the forwarder and how
  they authenticate deliveries; whether the forwarder keeps its own queue or relays
  synchronously.

## References

- [ADR-0008](0008-event-driven-architecture.md)
- [ADR-0010](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0011](0011-ai-agents-as-decoupled-service.md)
