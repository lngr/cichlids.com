---
id: TASK-3.34
title: Make outbox webhook delivery reliable and bounded to allowlisted targets
status: In Progress
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-12 12:42'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
  - app/mobile/e2e/playwright/webhook.write.mjs
parent_task_id: TASK-3
priority: high
ordinal: 57000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R4, R5, R10, R14; architecture: ADR-0024.

Webhook targets must be http or https on a host from the per-environment allowlist; redirects are not followed. Each event stores its next attempt time; the delay doubles from the previous attempt up to one hour, and an event is given up only after seven days. An administrator can redeliver given-up events. The dispatcher selects only due events through a partial index, reserves them in a short transaction, delivers outside the transaction with a ten-second timeout and records the result. A failure of one event never stops other events or the API host.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Given a webhook registration for a host outside the allowlist or with an ftp URL, When the administrator submits it, Then the API rejects it
- [x] #2 Given a subscriber that is down for two hours, When it comes back, Then it receives the events published in the meantime
- [x] #3 Given a corrupt event payload in the outbox, When the dispatcher runs, Then the API keeps serving requests and other events are delivered
- [x] #4 Given a given-up event, When the administrator triggers redelivery, Then the subscriber receives it
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach

#### Findings (state at main, 13e7b5e)
- `app/api/src/Cichlids.Api/Features/Webhooks/OutboxDispatcherHostedService.cs:22-29`: no try/catch around a pass, so any exception stops the host (R4).
- `app/api/src/Cichlids.Infrastructure/Outbox/OutboxDispatchService.cs`:
  - Line 42 opens a transaction; lines 44-51 lock `ORDER BY occurred_at LIMIT n` including rows that are not due (R14).
  - Line 63 computes due as `OccurredAt + Backoff` (R5).
  - Lines 92-114 make HTTP calls while row locks are held (R14).
  - Line 109 catches only `HttpRequestException` and `TaskCanceledException` (R4).
  - Lines 106 and 112 stop at the first failing subscriber.
  - Lines 119-132 give up after `MaxAttempts` and set `DispatchedAt`, so a given-up event cannot be told apart from a delivered one.
  - Line 151 `JsonNode.Parse` throws on a corrupt payload.
  - Lines 105 and 111 write the subscriber URL into `last_error`.
- `app/api/src/Cichlids.Infrastructure/Outbox/ServiceCollectionExtensions.cs:18`: 10 s timeout, but the default handler follows redirects (R10).
- `app/api/src/Cichlids.Api/Features/Webhooks/WebhookAdminEndpoints.cs:39`: only `Uri.TryCreate(..., Absolute)` (R4, R10).
- `app/api/src/Cichlids.Infrastructure/Persistence/Configurations/OutboxEventConfiguration.cs:9-25`: no index on open events (R14).
- `app/api/src/Cichlids.Infrastructure/Outbox/OutboxDispatcherOptions.cs:27`: `MaxAttempts = 10`, about 85 minutes (R5).
- `app/api/tests/Cichlids.Api.Tests/Features/Webhooks/OutboxDispatchServiceTests.cs:108-111` backdates `OccurredAt` and hides R5; lines 128-150 test concurrency with a single event (R42).

#### Target policy (R10, ADR-0024)
- New `WebhookTargetPolicy` in Infrastructure: absolute `http` or `https` URL, no userinfo, host (`Uri.IdnHost`, case-insensitive, exact match, no wildcards) listed in `Webhooks:AllowedTargetHosts`. The name avoids a clash with ASP.NET's top-level `AllowedHosts`.
- Enforced at registration (400) and again at delivery: a stored subscription that is no longer allowed counts as a failed attempt and is never contacted.
- Allowlist in Git: `appsettings.json` has an empty list (fail closed); `appsettings.Development.json` lists `localhost` and `127.0.0.1`.
- Startup validation rejects entries that are not bare host names; the effective allowlist is logged once at startup.

#### HTTP
- Named client with `SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = 2 min }`.
- Timeout from new option `DeliveryTimeout` (default 10 s).
- `HttpCompletionOption.ResponseHeadersRead`; the response body is never read.
- Every non-2xx response, including 3xx, is a failed attempt.

#### Schema
- New columns on `outbox_event`:
  - `next_attempt_at timestamptz NOT NULL`
  - `last_attempt_at timestamptz NULL`
  - `given_up_at timestamptz NULL`
  - `redelivered_at timestamptz NULL`
  - `acknowledged_subscription_ids bigint[] NOT NULL DEFAULT '{}'`
- Partial index `ix_outbox_event_due ON outbox_event (next_attempt_at) WHERE dispatched_at IS NULL AND given_up_at IS NULL`.

#### Schedule (R5)
- Pure `OutboxRetrySchedule.AfterFailure(attemptCount, attemptedAt, windowStart, options)` returns `(nextAttemptAt, givenUp)`.
- `nextAttemptAt = attemptedAt + min(5 s * 2^(n-1), MaxBackoff = 1 h)`, reusing `OutboxBackoff.Compute`.
- `givenUp = attemptedAt - windowStart >= GiveUpAfter` (default 7 days).
- `windowStart = redelivered_at ?? occurred_at`: an admin redelivery starts a fresh window.
- `MaxAttempts` is removed.

#### Reservation (R14): lease in next_attempt_at, fenced
- One autocommit statement reserves due events:
  `UPDATE outbox_event e SET next_attempt_at = @now + @lease FROM (SELECT id FROM outbox_event WHERE dispatched_at IS NULL AND given_up_at IS NULL AND next_attempt_at <= @now ORDER BY next_attempt_at LIMIT @batch FOR UPDATE SKIP LOCKED) d WHERE e.id = d.id RETURNING e.*`
- Delivery runs with no transaction and no lock.
- Each event's result is written in its own short `ExecuteUpdateAsync` guarded by `WHERE id = @id AND next_attempt_at = @reservedUntil AND dispatched_at IS NULL AND given_up_at IS NULL`. The returned lease value is the fencing token; a stale writer updates 0 rows and logs it.
- A crashed instance's events become due again when the lease expires (`ReservationLease`, default 5 min).
- Before delivering the next reserved event, a pass stops if the remaining lease is shorter than `DeliveryTimeout` times the number of matching subscribers; the rest is left to lease expiry.
- Why: SKIP LOCKED alone cannot work because R14 forbids holding locks during HTTP. A separate `locked_until` plus `lock_id` pair would add two columns and a second index predicate. Reusing `next_attempt_at` keeps one column, one partial index and one due predicate, and its value doubles as the fencing token. SKIP LOCKED stays inside the millisecond reservation statement so instances never block each other.

#### Time
- `TimeProvider` is injected (`TimeProvider.System` registered with TryAddSingleton).
- All comparisons use `@now` from the application, never SQL `now()`, so tests drive time with `FakeTimeProvider`.

#### Per-subscriber acknowledgement
- Every attempt goes to every matching subscriber that is not yet in `acknowledged_subscription_ids`.
- A 2xx response appends the subscription id (fenced).
- The event is dispatched once every currently matching subscriber has acknowledged.
- A healthy subscriber is never re-sent an event because another subscriber is down, and a failing subscriber never blocks another one.

#### Fault isolation (R4)
- Catch-all per subscriber call.
- Catch-all per event around envelope build, delivery and recording: a failure counts as a failed attempt of that event; if recording itself fails, the lease simply expires.
- The hosted loop catches every exception except shutdown cancellation, logs it and keeps polling.
- `last_error` carries the subscription id and the status or exception type, never the URL, truncated to 1000 characters.
- A pass that reserved a full batch runs again immediately (drain); otherwise it waits `PollInterval`.

#### Admin redelivery (ADR-0024, R5)
- New group `/api/admin/outbox` under the `Admin` policy:
  - `GET /api/admin/outbox/events?state=given-up&offset&limit` (paged with `Features/Common/Pagination.cs`).
  - `POST /api/admin/outbox/events/{id}/redeliver`: 202, 404 if unknown, 409 if the event is not given up.
  - `POST /api/admin/outbox/given-up/redeliver`: 202 with `{ count }`.
- Redelivery is one `ExecuteUpdateAsync`: `given_up_at = NULL, attempt_count = 0, last_error = NULL, next_attempt_at = @now, redelivered_at = @now`. Acknowledged subscribers are kept, so only subscribers that have not acknowledged receive it again.
- The event id never changes, so the envelope `id` stays stable (`OutboxDispatchService.cs:137-153`).

#### Producers
- `OutboxEvent.Create(...)` in Domain sets `NextAttemptAt = OccurredAt`; used by `CommentsWriteService.cs:132-144` and `PostPublishService.cs:58-66`.
- The DB default `now()` remains only as a safety net for raw inserts.

#### R42 scope
- The outbox parts of R42 (poison message, non-HTTP URL, backoff after an outage, a real concurrency test, plus the `HasPendingModelChanges` schema check) are delivered by this task. TASK-3.42 drops them from its scope.

### Steps

Each step is one commit: failing test first, then production change. New and changed C# doc comments use no inline tags (R43).

1. **Target policy (pure).**
   - Test: `app/api/tests/Cichlids.Infrastructure.Tests/Outbox/WebhookTargetPolicyTests.cs`. Cases: `ftp://` and `file:///` rejected; relative URL rejected; userinfo rejected; unlisted host rejected; case-insensitive match; IDN host compared as IdnHost; `http://allowed@evil.test` rejected; empty allowlist rejects everything.
   - Production: new `app/api/src/Cichlids.Infrastructure/Outbox/WebhookTargetOptions.cs` (section `Webhooks`, `AllowedTargetHosts`) and `WebhookTargetPolicy.cs`; registration with `ValidateOnStart` in `Outbox/ServiceCollectionExtensions.cs`; `appsettings.json` gets `"Webhooks": { "AllowedTargetHosts": [] }`, `appsettings.Development.json` gets `["localhost", "127.0.0.1"]`.
2. **Registration enforces the policy (AC1).**
   - Test: extend `app/api/tests/Cichlids.Api.Tests/Features/Webhooks/WebhookAdminEndpointsTests.cs:322-335` with `ftp://example.test/hook`, `file:///etc/passwd`, `http://169.254.169.254/latest` and `https://not-listed.test/hook` (all 400); allowlisted `https://example.test/hook` still returns 201. Add a startup test modeled on `ProductionSlugSecretTests.cs`: the host fails to start with an allowlist entry like `https://x`.
   - Production: `WebhookAdminEndpoints.cs:39` uses `WebhookTargetPolicy` and returns a clear 400 message.
   - Fixtures: `app/api/tests/Cichlids.Api.Tests/ApiFixture.cs:48-65` adds `Webhooks:AllowedTargetHosts:0 = example.test`; `Features/Webhooks/OutboxDispatchFixture.cs:40-54` adds `127.0.0.1`. In-memory config merges arrays by index over the Development list, so set every index explicitly.
3. **ADR-0024 clarification (docs only, no test).**
   - Add one sentence to the Redelivery section of `docs/adr/0024-webhook-delivery-boundary-and-redelivery.md`, for example: "A redelivery triggered by an administrator starts a new period, so the event is retried again until it is older than the configured period counted from the redelivery."
   - Timeless wording, no em or en dashes.
   - Regenerate the index with `python3 tools/generate_adr_index.py`; verify with `python3 tools/generate_adr_index.py --check`.
4. **Retry schedule (pure).**
   - Test: `app/api/tests/Cichlids.Infrastructure.Tests/Outbox/OutboxRetryScheduleTests.cs`. Cases: delay counts from `attemptedAt`, not `occurredAt`; delay doubles and is capped at 1 h; no give-up at 7 d minus 1 s even after 200 attempts (regression against `MaxAttempts`); give-up at or after 7 d; window restarts at `redeliveredAt`. Keep `OutboxBackoffTests.cs`.
   - Production: new `Outbox/OutboxRetrySchedule.cs`. In `OutboxDispatcherOptions.cs`, drop `MaxAttempts` (line 27); add `GiveUpAfter` (7 d), `ReservationLease` (5 min) and `DeliveryTimeout` (10 s), each validated positive.
5. **Schema: EF migration `AddOutboxRedeliverySchedule`.**
   - Test in `app/api/tests/Cichlids.Infrastructure.Tests/Persistence/SchemaMigrationTests.cs`:
     - A partial index on `outbox_event (next_attempt_at)` whose `indexdef` contains `WHERE ((dispatched_at IS NULL) AND (given_up_at IS NULL))`.
     - Column `acknowledged_subscription_ids` exists as `bigint[]` NOT NULL.
     - `HasPendingModelChanges()` is false (R42).
     - Backfill: a row with `last_error = 'gave up'` seeded before the migration ends with `given_up_at` set and `dispatched_at` NULL.
   - Production:
     - `Domain/Entities/OutboxEvent.cs`: new properties (`NextAttemptAt`, `LastAttemptAt`, `GivenUpAt`, `RedeliveredAt`, `AcknowledgedSubscriptionIds`) and a static `Create(...)`.
     - `OutboxEventConfiguration.cs`: columns, `HasDefaultValueSql("now()")` on `NextAttemptAt`, `HasDefaultValueSql("'{}'")` on the array, and `HasIndex(x => x.NextAttemptAt).HasDatabaseName("ix_outbox_event_due").HasFilter("dispatched_at IS NULL AND given_up_at IS NULL")`.
     - Generate with `dotnet ef migrations add AddOutboxRedeliverySchedule --project src/Cichlids.Infrastructure` (design-time factory `Persistence/CichlidsDbContextFactory.cs`).
     - Hand-written SQL in `Up` after the column adds: `UPDATE outbox_event SET next_attempt_at = occurred_at WHERE dispatched_at IS NULL`, then `UPDATE outbox_event SET given_up_at = dispatched_at, dispatched_at = NULL WHERE last_error = 'gave up'`.
     - Producers `CommentsWriteService.cs:135` and `PostPublishService.cs:58` switch to `OutboxEvent.Create`.
6. **Dispatcher: due-only reservation, lease, recording outside the transaction (R5, R14, AC2).**
   - Test infrastructure:
     - Add `Microsoft.Extensions.TimeProvider.Testing` to `Cichlids.Api.Tests.csproj`.
     - `OutboxDispatchFixture.CreateDispatchService(options, timeProvider)`.
     - `TestWebhookListener.cs` gets `SetDown(bool)` (503), a configurable status with `Location`, a hold-until-released mode and per-request receive records.
   - New tests in `OutboxDispatchServiceTests.cs`:
     - `OnlyDueEventsAreSelected`: BatchSize+5 older, not-due rows plus one due row; one pass delivers the due row.
     - `BackoffCountsFromTheLastAttempt`: an event that occurred 3 h ago fails once; a second pass at the same fake instant does not call the listener; after +5 s it does.
     - `SubscriberDownForTwoHoursReceivesEventsPublishedMeanwhile` (AC2): fake clock in 1 min steps; events at t0, t0+40 min and t0+100 min; after every failure assert `next_attempt_at - last_attempt_at` equals the schedule; the subscriber is back at t0+2 h; within at most 1 h of fake time all three are delivered exactly once with their ids, none given up.
     - `NoLockIsHeldDuringHttp`: while the listener holds the request, a second connection's `SELECT ... FOR UPDATE NOWAIT` on the row succeeds.
     - `ExpiredLeaseIsReservedByAnotherInstance`: instance A only reserves (simulated crash); after lease + 1 s, B delivers.
     - `StaleResultIsDiscarded`: A's late failure write after B's success leaves the row dispatched.
     - Rewrite `TwoConcurrentDispatchersDoNotDeliverTheSameEventTwice` (lines 128-150) with 60 events and 3 parallel instances; each id is delivered exactly once (R42).
     - Remove the backdating in `FailingSubscriberRetriesAndLaterSucceeds` (lines 108-111) and advance the fake clock instead.
   - Production: rewrite `OutboxDispatchService.cs` into `ReserveDueAsync`, `DeliverAsync` and `RecordAsync`; `RunOnceAsync` returns the number reserved; no `BeginTransactionAsync`; the reservation uses `FromSqlInterpolated` with `UPDATE ... RETURNING`, not composed, `AsNoTracking`.
7. **Per-event and per-subscriber isolation, no redirects, delivery-time policy (R4, R10).**
   - Tests:
     - `CorruptPayloadIsAFailedAttemptAndOthersAreDelivered`: raw SQL insert of a jsonb payload nested 100 levels deep (`JsonNode.Parse` exceeds MaxDepth 64); a normal event in the same batch is delivered; the corrupt one has `attempt_count = 1` and a `last_error`.
     - `NonHttpSubscriptionStoredBeforeTheAllowlistIsAFailedAttempt`: an `ftp://` subscription row inserted directly.
     - `RedirectIsNotFollowed`: listener A answers 302 to listener B; B receives nothing; `last_error` contains 302.
     - `SlowSubscriberTimesOut`: `DeliveryTimeout` 1 s with a held request is a failed attempt.
     - `LastErrorDoesNotContainTheUrl`.
   - Production: per-subscriber and per-event catch-alls in `OutboxDispatchService.cs`; delivery-time `WebhookTargetPolicy` check; `ServiceCollectionExtensions.cs:18` gets `ConfigurePrimaryHttpMessageHandler` with the handler from the Approach and the timeout from options.
8. **Per-subscriber acknowledgement (column added in step 5).**
   - Tests:
     - `AcknowledgedSubscriberIsNotCalledAgainWhileAnotherIsDown`.
     - `OneFailingSubscriberDoesNotBlockAnother`.
     - `EventIsDispatchedOnceEveryMatchingSubscriberAcknowledged`.
     - `RedeliveryGoesOnlyToSubscribersThatHaveNotAcknowledged` (completed in step 9).
   - Production: `DeliverAsync` skips acknowledged ids; `RecordAsync` appends ids with `array_append` under the same fencing predicate and sets `dispatched_at` once all matching subscribers are acknowledged.
9. **Admin redelivery (AC4).**
   - Test: new `app/api/tests/Cichlids.Api.Tests/Features/Webhooks/OutboxAdminEndpointsTests.cs` on `OutboxDispatchFixture`:
     - Anonymous 401, non-admin 403, moderator 403.
     - Unknown id 404; pending or dispatched event 409.
     - `GET state=given-up` lists the event with `givenUpAt` and `lastError`.
     - Single and bulk redeliver; the next pass delivers with the same envelope `id` and a valid HMAC.
     - A redelivered event that fails again is retried until the fresh window ends.
     - Add the new paths to `OpenApiDocumentTests.cs`.
   - Production: new `app/api/src/Cichlids.Infrastructure/Outbox/OutboxRedelivery.cs` (update logic) and `app/api/src/Cichlids.Api/Features/Webhooks/OutboxAdminEndpoints.cs` with DTOs in `WebhookDtos.cs`; mapped in `Program.cs:121`.
10. **Hosted loop never stops the host (AC3).**
    - Test: new `app/api/tests/Cichlids.Api.Tests/Features/Webhooks/OutboxDispatcherHostedServiceTests.cs`:
      - `ApiKeepsServingWhileTheLoopHitsACorruptEvent`: own `WebApplicationFactory` on the fixture container with `Enabled=true` and `PollInterval=200ms`; a corrupt and a good event; `/healthz` returns 200 repeatedly; the good event reaches the listener; `IHostApplicationLifetime.ApplicationStopping` is not triggered.
      - `DatabaseOutageIsLoggedAndPollingContinues`: hosted service with an unreachable connection string (`Port=1`); at least two error logs via `FakeLogger` (`Microsoft.Extensions.Diagnostics.Testing`); `ExecuteTask` is not faulted.
    - Production: `OutboxDispatcherHostedService.cs:22-29` gets a try/catch per pass (rethrow only when `stoppingToken` is cancelled), the drain loop and the startup log of the allowlist.
11. **End-to-end user path (DoD).**
    - Test: new `app/mobile/e2e/playwright/webhook.write.mjs`, tagged `// Story: TASK-3.34`; no browser, real Keycloak and API:
      1. Admin token by password grant for `dev-mod`.
      2. Registering `ftp://127.0.0.1/x` and `https://not-listed.test` returns 400 for both.
      3. A node `http` listener on 127.0.0.1 is registered (201).
      4. `dev-user` publishes a picture.
      5. The listener fails the first 2 deliveries, then receives `post.published` with a valid signature (compressed AC2).
      6. The listener switches to always 500; after another publish, wait until `GET /api/admin/outbox/events?state=given-up` lists the event.
      7. The listener switches to 200; `POST .../redeliver`; the listener receives the same `id` (AC4).
    - Production: `app/mobile/e2e/write-stack.sh:132` becomes `OutboxDispatcher__Enabled=true`, plus `OutboxDispatcher__PollInterval=00:00:01` and `OutboxDispatcher__GiveUpAfter=00:00:15`.
    - Task reference: `backlog task edit TASK-3.34 --ref app/mobile/e2e/playwright/webhook.write.mjs`.
12. **Client contract and docs.**
    - Regenerate `app/client-core/openapi.json` and `src/generated/` with `pnpm --filter @cichlids/client-core generate` against the dev API.
    - `app/stack/README.md`: the write stack runs the dispatcher; operator section on where the allowlist lives, how to list and redeliver given-up events, and the SQL to inspect pending events.
    - Update the class doc comments of the touched outbox files to the new behavior.

### Verification

- **AC1:**
  - Tests: `WebhookAdminEndpointsTests` (step 2), `WebhookTargetPolicyTests` (step 1), E2E `webhook.write.mjs`.
  - Operator: `POST /api/admin/webhooks` with an `ftp://` URL or an unlisted host returns 400; the API startup log shows the effective allowlist.
- **AC2:**
  - Tests: `SubscriberDownForTwoHoursReceivesEventsPublishedMeanwhile` (fake clock) and the compressed outage in the E2E.
  - Operator: stop the subscriber in the dev stack; `SELECT id, attempt_count, last_attempt_at, next_attempt_at, given_up_at, acknowledged_subscription_ids FROM outbox_event WHERE dispatched_at IS NULL;` shows gaps doubling up to 1 h and `given_up_at` NULL; delivery arrives after the restart.
- **AC3:**
  - Tests: `ApiKeepsServingWhileTheLoopHitsACorruptEvent`, `CorruptPayloadIsAFailedAttemptAndOthersAreDelivered`, `DatabaseOutageIsLoggedAndPollingContinues`.
  - Operator: insert a deeply nested jsonb row into the dev database; the API log shows a warning, `/healthz` stays 200, and the next comment event is delivered.
- **AC4:**
  - Tests: `OutboxAdminEndpointsTests` and the E2E.
  - Operator: `GET /api/admin/outbox/events?state=given-up`, then `POST /api/admin/outbox/events/{id}/redeliver`; the subscriber log shows the same event id.
- **R14 index:** schema test. Operator: `SET enable_seqscan = off; EXPLAIN <reservation subquery>` shows `Index Scan using ix_outbox_event_due`.
- **R10 redirects and timeout:** `RedirectIsNotFollowed`, `SlowSubscriberTimesOut`.
- **ADR:** `python3 tools/generate_adr_index.py --check` passes.
- **Suites:** `dotnet test app/api/Cichlids.sln`, `pnpm --filter mobile e2e:write`, `node tools/check-story-test-binding.mjs`, and the CI jobs from TASK-2.28 once they exist.

### Risks

- **Clock skew:** leases and due times use each instance's clock. Skew of seconds is small against a 5 min lease; keep NTP on the nodes.
- **Duplicates:** a crash or an expired lease can deliver an event twice. ADR-0024 accepts this (at-least-once, stable id).
- **Order:** delivery follows `next_attempt_at`, not occurrence, after an outage; subscribers must not rely on order.
- **EF reservation query:** `FromSql` with `UPDATE ... RETURNING` must not be composed. Fallback: `Database.SqlQuery<Guid>` plus a load.
- **Test config arrays:** in-memory config overrides arrays by index over `appsettings.Development.json`; set test allowlists explicitly.
- **Empty production allowlist:** registration is impossible until TASK-2.4 or TASK-2.29 add hosts. Fail closed is intended.
- **Subscription changes:** a subscription added after an event was published also receives that event while it is still pending, because matching is evaluated per attempt. Subscribers are idempotent, so this is acceptable.
- **E2E timing:** the E2E depends on real backoff (about 30 s); use generous waits.
- **Outbox growth:** dispatched rows are never cleaned up. The partial index keeps the dispatcher fast; retention is a follow-up.
- **Order with other tasks:**
  - **Parallel migrations:** TASK-3.35, TASK-3.36 and TASK-3.39 may add migrations. The task merged second deletes its migration and regenerates it on the rebased base; `CichlidsDbContextModelSnapshot.cs` is never hand-merged.
  - **TASK-2.28:** its CI jobs run these tests. If it moves the API into a container, the listener address in `webhook.write.mjs` and the e2e allowlist must change with it.
  - **TASK-2.29:** consumes the envelope and the `X-Cichlids-Signature` header unchanged.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### What the task delivers

- **Target allowlist:** `WebhookTargetPolicy` accepts an absolute `http` or `https` URL without user information whose host (ASCII form, exact, case-insensitive) is listed in `Webhooks:AllowedTargetHosts`. `appsettings.json` has an empty list, `appsettings.Development.json` lists `localhost` and `127.0.0.1`. Entries that are not bare host names or IP addresses fail the startup. `POST /api/admin/webhooks` returns 400 for any other target, and the dispatcher never contacts a stored subscription outside the list (a failed attempt `subscription {id} target is not allowed`). The API logs the effective list at startup.
- **HTTP:** the delivery client follows no redirects, keeps no cookies, recycles pooled connections after two minutes and reads only the response headers. Each request is bounded by `OutboxDispatcher:DeliveryTimeout` (10 s). Every non-2xx response, 3xx included, is a failed attempt.
- **Schema:** migration `AddOutboxRedeliverySchedule` adds `next_attempt_at`, `last_attempt_at`, `given_up_at`, `redelivered_at` and `acknowledged_subscription_ids bigint[]` to `outbox_event`, plus the partial index `ix_outbox_event_due (next_attempt_at) WHERE dispatched_at IS NULL AND given_up_at IS NULL`. Open rows become due at once; rows marked dispatched with `last_error = 'gave up'` move to `given_up_at`. Producers create events through `OutboxEvent.Create`, which makes them due when they occur.
- **Schedule:** `OutboxRetrySchedule` counts the delay from the failed attempt (5 s doubling, capped by `MaxBackoff`, 1 h). An event is given up once `GiveUpAfter` (7 d) has passed since `redelivered_at ?? occurred_at`, whatever the attempt count. `OutboxBackoff` returns its ceiling for any attempt count, also beyond about 60 attempts where the doubled delay exceeds a TimeSpan.
- **Dispatcher:** one autocommit `UPDATE ... FROM (SELECT ... ORDER BY next_attempt_at LIMIT n FOR UPDATE SKIP LOCKED) ... RETURNING` reserves due events by moving `next_attempt_at` to the end of `ReservationLease` (5 min). Delivery runs without a transaction or lock. Each result is written by an `ExecuteUpdateAsync` fenced by the lease value, so a late result after another instance took over is discarded and logged. An event gets as many deliveries as fit into the remaining lease (one `DeliveryTimeout` kept for recording), the first event of a pass at least one; subscribers left out count as a failed attempt and come first on a later attempt (rotation by attempt count). A pass that runs out of lease stops with a warning; reservation order puts events without an attempt, then those attempted longest ago, first, so leftover events are not starved. `ReservationLease` must be at least three times `DeliveryTimeout`. Time comes from an injected `TimeProvider` (application time in every comparison).
- **Per-subscriber acknowledgement:** each attempt goes to the matching subscribers missing from `acknowledged_subscription_ids`; a 2xx adds the id; the event is dispatched once every matching subscriber has acknowledged it. The ids are written as the union under the same fence (equivalent to `array_append` under the fence).
- **Fault isolation:** catch-alls per subscriber and per event (envelope build, delivery, recording); `last_error` names the subscription and the status or exception type (never the URL), truncated to 1000 characters. The hosted loop logs a failing pass and keeps polling, drains at once after a full batch and otherwise waits `PollInterval`.
- **Admin redelivery:** `GET /api/admin/outbox/events?state=given-up&offset&limit`, `POST /api/admin/outbox/events/{id}/redeliver` (202, 404, 409) and `POST /api/admin/outbox/given-up/redeliver` (202 with `count`) under the `Admin` policy. On dispatch `last_error` is cleared and `last_attempt_at` is the successful attempt; an event without pending subscribers is dispatched without building its body. A redelivery clears `given_up_at`, `attempt_count` and `last_error`, sets `next_attempt_at` and `redelivered_at` to now and keeps the acknowledged subscriptions; the envelope `id` stays the event id.
- **Options:** `OutboxDispatcher` gains `GiveUpAfter`, `ReservationLease` and `DeliveryTimeout`; all durations and `BatchSize` are validated positive at startup.
- **Write stack:** `write-stack.sh` runs the dispatcher with `PollInterval=00:00:01` and `GiveUpAfter=00:00:15`.
- **Client and docs:** OpenAPI document and generated client regenerated (`listOutboxEvents`, `redeliverOutboxEvent`, `redeliverGivenUpOutboxEvents`); ADR-0024 states that a redelivery starts a new period; `app/stack/README.md` describes the allowlist, the admin redelivery and the SQL for open events.

### Operator notes

- The dev database `cichlids` needs the migration before the dev API is started on this code: `cd app/api && DOTNET_ENVIRONMENT=Development ~/.dotnet/dotnet run --project src/Cichlids.Etl -- migrate`.
- The production allowlist is empty, so no webhook can be registered in staging or production until TASK-2.4 or TASK-2.29 add the hosts to the environment configuration in Git.

### Operator checks

`TOKEN` is an admin access token (password grant for `dev-mod`, see `app/stack/README.md`).

- **AC1:** `curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"url":"ftp://127.0.0.1/x","secret":"s","eventTypes":["*"]}' http://localhost:5045/api/admin/webhooks` gives 400, as does `https://not-listed.test/hook`. The API startup log shows `Webhook target allowlist: localhost, 127.0.0.1`.
- **AC2:** register a local listener, stop it, publish pictures; `select id, attempt_count, last_attempt_at, next_attempt_at, given_up_at, acknowledged_subscription_ids from outbox_event where dispatched_at is null;` shows gaps doubling up to one hour and `given_up_at` null; after the listener restarts the events arrive.
- **AC3:** insert a deeply nested jsonb row into the dev database (`insert into outbox_event (id, occurred_at, event_type, aggregate_type, aggregate_id, payload, next_attempt_at) values (gen_random_uuid(), now(), 'test.event', 'post', '1', (repeat('[', 100) || repeat(']', 100))::jsonb, now())`); the API log shows a warning, `/healthz` stays 200 and the next comment event is delivered.
- **AC4:** `curl -H "Authorization: Bearer $TOKEN" 'http://localhost:5045/api/admin/outbox/events?state=given-up'`, then `curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:5045/api/admin/outbox/events/ID/redeliver`; the subscriber log shows the same event id.
- **Index:** `SET enable_seqscan = off; EXPLAIN SELECT id FROM outbox_event WHERE dispatched_at IS NULL AND given_up_at IS NULL AND next_attempt_at <= now() ORDER BY next_attempt_at LIMIT 20 FOR UPDATE SKIP LOCKED;` shows `Index Scan using ix_outbox_event_due`.

### Acceptance criteria status

- Checked by automated tests: #1 (WebhookTargetPolicyTests, WebhookAdminEndpointsTests, webhook.write.mjs), #2 (OutboxDispatchServiceTests.RunOnceAsync_SubscriberDownForTwoHoursReceivesEventsPublishedMeanwhile on a fake clock, compressed outage in webhook.write.mjs), #3 (OutboxDispatcherHostedServiceTests.ApiKeepsServingWhileTheLoopHitsACorruptEvent and DatabaseOutageIsLoggedAndPollingContinues, OutboxDispatchServiceTests.RunOnceAsync_CorruptPayloadIsAFailedAttemptAndOthersAreDelivered), #4 (OutboxAdminEndpointsTests, webhook.write.mjs).
- The operator checks above repeat them on the dev stack.

### Tests

- `app/api/tests/Cichlids.Infrastructure.Tests`: Outbox/WebhookTargetPolicyTests.cs, Outbox/OutboxRetryScheduleTests.cs, Outbox/OutboxDispatcherOptionsValidatorTests.cs, Outbox/OutboxBackoffTests.cs, Persistence/SchemaMigrationTests.cs (partial index, array column, no pending model changes, backfill)
- `app/api/tests/Cichlids.Api.Tests`: Features/Webhooks/OutboxDispatchServiceTests.cs, Features/Webhooks/OutboxAdminEndpointsTests.cs, Features/Webhooks/OutboxDispatcherHostedServiceTests.cs, Features/Webhooks/WebhookAdminEndpointsTests.cs, WebhookTargetAllowlistStartupTests.cs, OpenApiDocumentTests.cs
- E2E: `app/mobile/e2e/playwright/webhook.write.mjs` through `pnpm --dir app/mobile run e2e:write`
<!-- SECTION:NOTES:END -->
