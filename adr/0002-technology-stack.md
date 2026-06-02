# ADR-0002: Technology Stack

- **Status:** Accepted
- **Date:** 2026-06-02
- **Deciders:** Alexander Langer

## Context and Problem Statement

For rebuilding the mobile-first photo-sharing community described in
[ADR-0001](0001-purpose-and-scope.md), the technical foundation must be set. The
application is **mobile-first** (Apple App Store and Google Play), additionally offers an
**equivalent web channel**, serves a **global user base** (focus USA), and is developed
**entirely with coding agents**.

This yields the governing selection criteria:

1. **Coding-agent suitability** — widespread, well-documented frameworks with a large
   training corpus (top criterion).
2. **Store suitability** on iOS and Android (free, review-compliant).
3. **Mobile-first**, web as a second, equivalent channel.
4. **Global scaling and cost efficiency** — the architecture must allow globally low latency
   at manageable cost (detail in [ADR-0009](0009-hosting-and-global-delivery.md)).
5. **Reviewability by the team** — the decider must be able to soundly review agent output.
6. **Fitness for a thoroughly event-driven architecture**
   ([ADR-0008](0008-event-driven-architecture.md)).

This decision is deliberately **high-level**. Data model, persistence tooling, broker, and
other detail questions are **not** fixed here (see "Consequences").

## Decision

| Layer | Commitment |
|---|---|
| **Mobile / Frontend** | **React Native + Expo** (with Expo Router). Native apps for iOS and Android; web as a secondary channel via React Native Web or — if web requirements grow — a separate web application. |
| **Authoritative domain logic** | **Server-side in C#.** Reputation/rating computation, feed/ranking logic, moderation rules, and business validation are the **single authoritative source of truth** and live in the .NET backend. Clients display values delivered by the server. Offline, **raw data/drafts** are captured and processed server-side after sync — **no** second implementation of the authoritative logic in the client. |
| **Client shared core** | A framework-agnostic **TypeScript core for client concerns only**: the API client generated from the OpenAPI document including types, offline sync/caching, presentation/UI logic, and pure UX validation. **No** authoritative domain logic. Shared by Mobile **and** Web. |
| **Backend** | **ASP.NET Core (C#), .NET 10**, API-first (REST). The OpenAPI document is produced with the built-in `Microsoft.AspNetCore.OpenApi`; the interactive reference UI is provided by **`Scalar.AspNetCore`**. **No NSwag.** The typed TypeScript clients are generated from this OpenAPI document. |
| **Database platform** | **PostgreSQL** as the DBMS platform. Backup, replication, and recovery are binding and specified in [ADR-0013](0013-backup-and-disaster-recovery.md). |
| **Auth / Identity** | **Keycloak** as its own service (OIDC/JWT). Replaces the legacy application's **Auth0** integration; existing accounts are migrated into Keycloak. Binds the backend, Mobile/Web, and the service identities of external services (including the AI agents from [ADR-0011](0011-ai-agents-as-decoupled-service.md)). |
| **Hosting / Deployment** | **Cloud, globally delivered, cost-efficient** — set in [ADR-0009](0009-hosting-and-global-delivery.md); infrastructure declaratively as code via strict GitOps in [ADR-0010](0010-gitops-and-infrastructure-as-code.md). |

## Considered Options

### Backend language & framework

- **ASP.NET Core / C# (chosen).**
  - *Pro:* the decider's deepest experience → sound reviewability of agent output;
    statically typed, a lean and stable dependency base, LTS releases; runs first-class on
    Linux/Docker; built-in OpenAPI generation; very well suited to an event-driven server
    architecture.
  - *Con:* not the same language as the frontend → no shared source for types (mitigated by
    clients generated from the OpenAPI document).
- **TypeScript / NestJS.**
  - *Pro:* one language across Mobile, Web, and backend → directly shared types/schemas;
    largest coding-agent corpus; high velocity.
  - *Con:* a considerably larger, faster-moving dependency base (npm); less of the decider's
    own backend experience. The point that originally favored TypeScript — proximity to the
    AI/LLM ecosystem — falls away, because the AI agents deliberately run as a **separate**
    service ([ADR-0011](0011-ai-agents-as-decoupled-service.md)).
- **Backend-as-a-Service (e.g. Supabase).**
  - *Pro:* very fast to first results.
  - *Con:* weaker control over a thoroughly event-driven architecture and over the cost
    profile for large image volumes; higher lock-in.

### Mobile delivery

- **React Native + Expo (chosen).**
  - *Pro:* native apps → safe store approval; native push/offline/camera path (central to
    capturing and composing posts); OTA updates without re-review; very large coding-agent
    corpus (React + TypeScript); web representable as a secondary channel.
  - *Con:* the web channel via React Native Web is secondary and may later need a separate
    web application.
- **Capacitor + React.**
  - *Pro:* one web codebase serves web and mobile.
  - *Con:* WebView-based → higher risk at store review; push/offline/camera only via
    plugins.
- **Flutter.**
  - *Con:* smaller coding-agent corpus (Dart), weaker web target, different language realm
    than the client core.
- **Pure PWA.**
  - *Con:* insufficient/uncertain approval in the Apple App Store; misses the store
    requirement and the native camera/offline path.

## Rationale

1. **Coding-agent suitability** is paramount: React Native + Expo and ASP.NET Core are
   widespread and excellently documented.
2. **Reviewability:** C#/.NET offers static typing, a lean and stable dependency base, and
   LTS — and is the language in which the decider can best review agent output.
   Consequently the authoritative domain logic (reputation, ranking, moderation) lives in
   the C# backend, not in the TypeScript client.
3. **Clean interface:** API-first with one OpenAPI document as the contract gives the app
   typed, generated clients and is at the same time the natural seam for external services
   (AI agents) and an event-driven architecture.

## Consequences

- **Positive:** a thoroughly agent-friendly, widespread stack; clean separation of
  authoritative server logic and client concerns; safe store approval; OpenAPI as a stable
  contract for app, web, and external services.
- **Negative / Trade-offs:** two language realms (C# backend, TypeScript client) instead of
  one; shared types arise only generated via OpenAPI, not through common source; the web
  channel is initially secondary.
- **To be decided later (own ADRs):**
  - **Data model & schema** — redesigned versus the legacy application, with a `legacy_id`
    bridge for migration.
  - **Persistence/migration tooling** (e.g. EF Core or an alternative).
  - **Concrete message broker / queue** for the event-driven architecture
    ([ADR-0008](0008-event-driven-architecture.md)).

## References

- [ADR-0001: Purpose and Scope of the Application](0001-purpose-and-scope.md)
- [ADR-0008: Event-Driven Architecture](0008-event-driven-architecture.md)
- [ADR-0009: Hosting and Global Delivery](0009-hosting-and-global-delivery.md)
- [ADR-0010: GitOps and Infrastructure as Code](0010-gitops-and-infrastructure-as-code.md)
- [ADR-0011: AI Agents as a Decoupled Service](0011-ai-agents-as-decoupled-service.md)
- [ADR-0013: Backup and Disaster Recovery](0013-backup-and-disaster-recovery.md)
