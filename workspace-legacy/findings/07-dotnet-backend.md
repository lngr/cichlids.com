# 07 – The .NET Backend (`cichlids.backend`)

Forensic, read-only analysis of the previously-missing .NET backend source tree at
`/workspaces/legacy-source/cichlids.backend`.

## TL;DR

A bespoke **CQRS / event-sourcing framework built on Microsoft Orleans 2.1.2**
(actors = "grains"), targeting **.NET Core 2.0 / .NET Standard 2.0**, persisting
to **MySQL/MariaDB**, fronted by an **ASP.NET Core Web API** secured with **Auth0
JWT bearer**. The framework ("Cichlids.Platform") is comparatively mature, but
**the cichlids domain itself was never written**: the only domain code is a
`Cichlids.Domain.Scratch` "HelloWorld" sample. There are **no User, Picture,
Comment, Tank, Species (etc.) aggregates** — they were never built. The project
is an **abandoned greenfield rewrite at the framework-plumbing stage**, with a
~5-day commit window (2018-11-15 → 2018-11-20).

---

## 1. Overall Architecture

Two conceptual layers:

### Platform / framework (generic, domain-agnostic)
- **`Cichlids.Core`** (`netstandard2.0`) — pure domain abstractions, no Orleans
  dependency. Contains:
  - `Domain/Aggregates` — `Aggregate<TRoot,TId>`, `IAggregateContext`, command
    routing (`On<TCommand>(...).Do(...)`).
  - `Domain/Projections` — `Projection<TReadModel,TId>` read-model builders.
  - `Domain/Sagas` — `Saga`, `SagaRootEntity`, process managers that can record
    follow-up commands.
  - `Domain/Messages` — `ICommand`, `IEvent`, attributes `[DomainCommand("...")]`,
    `[DomainEvent("...")]`, and the built-in `PublishEventAt` command.
  - `Domain/Shared` — `Identity<T>` (GUID value-object IDs), `ValueObject`,
    `[EventStream]`, `[ReadModel]`, `[StreamConsumer]` attributes.
  - `AggregateSource/` — a vendored copy of the open-source **AggregateSource**
    library (`LICENSE.txt` present) for the `AggregateRootEntity` / `EventRecorder`
    / `InstanceEventRouter` event-application machinery.
- **`Cichlids.Platform`** (`netstandard2.0`) — the Orleans runtime implementation
  of the above. Key grains/services (`Cichlids.Platform/...`):
  - **CommandBus**: `CommandBusGrain` (`[StatelessWorker][Reentrant]`) →
    `CommandHandlerRegistry` dispatch → `AggregateCommandHandlerGrain`.
  - **Aggregates**: `AggregateGrain<TRoot,TId>` — an Orleans `JournaledGrain`
    using `ICustomStorageInterface` + a `LogConsistencyProvider`
    (`Constants.CichlidsEventStorage`) for event-sourced state.
  - **EventStore**: `EventStreamGrain`, `EventStoreService`, `EventCacheGrain`,
    `StoredEventGrain`, `MysqlEventStreamStorage` (Dapper-based MySQL persistence).
  - **Projections**: `ProjectionGrain<...>` writing read models via
    `MysqlDocumentStorage` ("ReadModelStorage" grain storage).
  - **Sagas**: `SagaGrain<...>` (also a `JournaledGrain`) that consumes events and
    emits commands back onto the command bus.
  - **StreamConsumer**: subscription/linking machinery (`StreamLinker`,
    `StreamSubscription`, `ConsumerStreamIdSelector`) connecting event streams to
    projections/sagas. Uses Orleans `SimpleMessageStreamProvider` (in-memory).
  - **Scheduler**: `EventSchedulerGrain` + `ScheduleRemindersGrain` (Orleans
    reminders) implementing delayed/`PublishEventAt` event publication.
  - **Serialization**: `PlatformSerializer` (Newtonsoft) with value-object and
    private-setter converters.
- **`Cichlids.Platform.Interfaces`** (`netstandard2.0`) — grain interfaces /
  message envelopes (`ICommandBus`, `CommandEnvelope`, `EventEnvelope`,
  `IMessageContext`) shared between silo and clients.

### Hosting / wiring
- **`Cichlids.Backend`** — `CichlidsBackendSiloHost` builds the Orleans
  `SiloHostBuilder` (localhost clustering, ClusterId `"cichlids"`, ServiceId
  `"Cichlids.Backend"`), registers storage providers, runs `PlatformStartupTask`.
  `AppDb` opens the MySQL connection. `AggregateRegistryFactory`,
  `ProjectionRegistryFactory`, `SagaRegistryFactory` register the domain — **all
  three register only the HelloWorld scratch types.**
- **`Cichlids.Api`** — ASP.NET Core (`netcoreapp2.0`) host. `Program.cs` starts
  the silo **in-process** then starts the Web API. `Startup.cs` configures Auth0
  JWT bearer auth + permissive CORS.
- **`Cichlids.Backend.Client`** — `BackendClient` / `IBackendClient`: the public
  façade (`Task Execute(ICommand)`) that wraps a command in a `CommandEnvelope`
  and dispatches to the `CommandBus` grain.
- **`Cichlids.Backend.Grains`** — only `TestCommandHandler` (a no-op stub).
- **`Cichlids.TestClient`** — console app that fires `SayHello` commands.

Citations: `Cichlids.Backend/CichlidsBackendSiloHost.cs`,
`Cichlids.Api/Program.cs`, `Cichlids.Api/Startup.cs`,
`Cichlids.Backend.Client/BackendClient.cs`,
`Cichlids.Platform/Aggregates/AggregateGrain.cs`,
`Cichlids.Platform/CommandBus/CommandBusGrain.cs`,
`Cichlids.Platform/Sagas/Grain/SagaGrain.cs`.

---

## 2. Domain Model — Aggregates / Entities / Grains

**There is no cichlids business domain in this tree.** No `User`, `Picture`,
`Comment`, `Tank`, `Species`, `Album`, `Vote`, etc. The only domain artefacts are
the sample/"scratch" project `Cichlids.Domain.Scratch`:

| Aggregate / type | Kind | Stream / read-model name | Source |
|---|---|---|---|
| `HelloWorld` (`HelloWorldAggregate`) | Aggregate root | `hello_world` | `Cichlids.Domain.Scratch/Aggregate/` |
| `HelloProcess` (`HelloWorldSaga`) | Saga / process manager | `hello_process` | `Cichlids.Domain.Scratch/Sagas/` |
| `HelloEntity` (`HelloProjection`) | Read model / projection | `hello_entity` / `hello_projection` | `Cichlids.Domain.Scratch/Projection/` |
| `HelloId` | Value-object identity | — | `Cichlids.Domain.Scratch/Aggregate/HelloId.cs` |

These exist solely to exercise the platform (the `rm_hello_entity` table in
`cichlids.sql` is its read-model store).

---

## 3. Events and Commands

The **complete** set of domain messages found anywhere in the tree:

### Commands (`ICommand`)
| Command | `[DomainCommand]` name | Source | Notes |
|---|---|---|---|
| `SayHello` | `say_hello` | `Cichlids.Domain.Scratch/Aggregate/SayHello.cs` | scratch |
| `TestCommand` | (none) | `Cichlids.Domain.Scratch/Sagas/TestCommand.cs` | scratch, no-op handler |
| `PublishEventAt` | `publish_event_at` | `Cichlids.Core/Domain/Messages/PublishEventAt.cs` | **platform** (delayed publish) |

### Events (`IEvent`)
| Event | `[DomainEvent]` name | Source | Notes |
|---|---|---|---|
| `StartedHello` | `started_hello` | `Cichlids.Domain.Scratch/Aggregate/StartedHello.cs` | scratch |
| `SaidHello` | `said_hello` | `Cichlids.Domain.Scratch/Aggregate/SaidHello.cs` | scratch |
| `HelloProcessStarted` | `hello_process_started` | `Cichlids.Domain.Scratch/Sagas/HelloProcessStarted.cs` | scratch |
| `HelloProcessAufgeweckt` | `hello_process_aufgeweckt` | `Cichlids.Domain.Scratch/Sagas/HelloProcessAufgeweckt.cs` | scratch |

Plus **platform-internal** event-store events (not domain events):
`EventStreamCreated`, `EventStreamUpdated`, `EventLinkedToStream`, `EventWritten`
(`Cichlids.Platform/EventStore/`).

**Implication:** the message catalog reveals **no cichlids feature was
implemented or even modelled** — only generic event-sourcing plumbing plus a
counter demo. There is no evidence (in events/commands) of upload, comment,
rating, tank, or species features being built or planned in code.

---

## 4. API Surface (`Cichlids.Api`)

Minimal and unfinished:
- `ValuesController` (`api/values`, `Cichlids.Api/ValuesController.cs`):
  - `GET api/values` `[Authorize]` — echoes the caller's email claim from the
    Auth0 JWT (or `"ne, keine email"`). A connectivity/auth smoke test.
  - `GET api/values/{id}` — returns a random GUID; the grain call is commented out.
  - `POST api/values/{id}` — empty; the grain call is commented out.

No controllers expose any cichlids operation. The API is effectively an Auth0 +
Orleans wiring proof-of-concept.

---

## 5. Mapping to Legacy Data

- **No mapping exists.** The backend never references the TYPO3 `user_cichlids_*`
  tables, nor any cichlids columns. A tree-wide grep for `user_cichlids`,
  `picture`, `species`, `comment`, `tank`, `fish`, `breed`, `s3`, `sqs`, `aws`
  returns **nothing** beyond the Auth0 config string.
- `cichlids.sql` (`Cichlids.backend/cichlids.sql`, dumped 2018-11-12) is the
  **event-store schema for this backend**, not the legacy app DB. Its tables are
  exactly what the platform writes:
  - `events`, `event_streams`, `event_stream_events` — the append-only event store.
  - `stream_consumers`, `stream_consumer_types`, `stream_consumer_events`,
    `stream_consumer_subscriptions` — the StreamConsumer subscription/linking model.
  - `scheduled_events` — the `EventSchedulerGrain` store.
  - `rm_hello_entity` — the **HelloProjection** read model (the only read model).
- **Match against the restored empty event-store:** the schema matches the
  platform's expectations. Note `stream_consumer_events.AUTO_INCREMENT=242239`
  in the dump — i.e. the developer's local DB had run the HelloWorld load/soak
  test many times; this is **demo/throwaway data, not production cichlids data**.
  No `rm_*` table for any real cichlids read model exists, confirming nothing
  domain-specific was ever projected.

---

## 6. Persistence / Infrastructure / Config

- **Event store / read models:** MySQL/MariaDB via Dapper
  (`MysqlEventStreamStorage`, `MysqlDocumentStorage`). Connection from env var
  `MYSQL_CONNECTION_STRING`, fallback `SERVER=127.0.0.1;...DATABASE=cichlids;UID=root;PASSWORD=`
  (`Cichlids.Backend/AppDb.cs`).
- **Orleans clustering:** localhost only (`UseLocalhostClustering`), in-memory
  reminders ("AdoNet version broken with Mysql" per code comment), in-memory
  stream provider and PubSubStore. Not configured for a real multi-node cluster.
- **Auth:** Auth0 JWT bearer (`Cichlids.Api/Startup.cs`,
  `Cichlids.Api/appsettings.json`). Config names only (no secrets present):
  `Auth0:Domain` = `cichlids.eu.auth0.com`, `Auth0:Audience`. Consistent with the
  Auth0 usage documented in `05-auth0-users.md`.
- **S3 / SQS / AWS:** **none.** No object storage or message-queue integration
  anywhere in the source or packages.
- **CI / deploy:** Bitbucket Pipelines (`bitbucket-pipelines.yml`) builds a Docker
  image `cloud.canister.io:5000/redrainbow/cichlids.api` and (manual trigger)
  rsync+ssh deploys `docker-compose.yml` to a prod host. Secrets referenced **by
  name only**: `DOCKER_HUB_USERNAME`, `DOCKER_HUB_PASSWORD`, `PROD_DEPLOY_SSH`,
  `PROD_DEPLOY_DIR`. `docker-compose.yml` has `MYSQL_CONNECTION_STRING=fixme`
  (placeholder — never finished). `docker-compose.override.yml` points at a dev
  host `mbp`.
- **Note** the repo slug `redrainbow` and macOS artefacts (`.DS_Store`,
  `Platform.sln.DotSettings.user`, `.idea/`) indicate a single developer working
  on a Mac with JetBrains Rider.

---

## 7. State of Completeness

**Verdict: abandoned prototype / framework spike. Not production-ready, no
cichlids domain implemented.**

Evidence:
- **Timeline:** entire git history spans **2018-11-15 → 2018-11-20** (64 commits,
  ~5 days). First/last commit dates from `git log`.
- **Commit content:** the last ~20 commits are almost entirely CI/Docker/pipeline
  plumbing ("Setup CD", "Give docker more memory", "Fix dockerfile", "Docker:
  correct image name"); the substantive platform work predates them. No commit
  introduces a real domain type.
- **`Cichlids.Domain.Scratch`** is the only domain — explicitly a scratch/spike
  project; aggregate/projection/saga registries register only `HelloWorld*`.
- **API** is the `ValuesController` template with grain calls commented out.
- **`TestCommandHandler`** is a no-op (handler body commented out).
- **Unfinished markers:** `MYSQL_CONNECTION_STRING=fixme` in
  `docker-compose.yml`; "AdoNet version broken with Mysql" / "deadlocks in Mysql"
  comments; in-memory-only Orleans config.
- **Tests** exist only for the platform/framework (`Cichlids.Core.Tests`,
  `Cichlids.Core.Testing.Tests`, `Cichlids.Platform.Tests` — value-object
  serialization and the vendored AggregateSource test specs). **No domain tests.**

In short: a developer built a genuinely capable Orleans-based CQRS/ES platform,
proved it out with a HelloWorld counter and a CI/Docker deploy pipeline, and
stopped **before writing a single line of cichlids.com business logic.** This
backend was never a functioning replacement for the TYPO3 app.

---

## Appendix: Tech-stack summary

- **Runtime:** .NET Core 2.0 (API/host) / .NET Standard 2.0 (libraries).
- **Actor framework:** Microsoft Orleans 2.1.2 (`JournaledGrain` event sourcing,
  `ICustomStorageInterface`, custom `LogConsistencyProvider`, reminders, streams).
- **Persistence:** MySQL/MariaDB via Dapper + `MySqlConnector`/`MySql.Data`.
- **Serialization:** Newtonsoft.Json.
- **Web:** ASP.NET Core MVC + JWT bearer (Auth0).
- **Build/CI:** Docker, Bitbucket Pipelines, canister.io registry.
- **Vendored:** AggregateSource (event-application library) under `Cichlids.Core`.
