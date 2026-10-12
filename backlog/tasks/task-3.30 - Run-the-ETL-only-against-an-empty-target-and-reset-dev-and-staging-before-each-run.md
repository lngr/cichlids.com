---
id: TASK-3.30
title: >-
  Run the ETL only against an empty target and reset dev and staging before each
  run
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-12 06:41'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: high
ordinal: 52000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R3, R18, R38.

The ETL writes only into an empty target database and aborts before its first write otherwise. Dev and staging get a reset that empties the database and the Keycloak realm users and rebuilds the schema, so every run starts from the same state and rehearses the go-live. The reset refuses to run in Production. Media objects stay in the object store across resets. The dry-run mode is removed. bootstrap.sh fails clearly when Postgres does not come up and imports the realm fresh after a reset.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a target database that holds any row, When the operator runs the full ETL, Then it aborts before writing and names the reason
- [ ] #2 Given the dev stack, When the operator runs the reset and then the full ETL and the account import, Then both complete and the verify step reports no mismatch
- [ ] #3 Given the Production environment setting, When the operator invokes the reset, Then it refuses to run
- [ ] #4 Given the ETL command line, When the operator passes --dry-run, Then the option is rejected as unknown
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

- Prerequisite: TASK-2.27 lands first. This plan builds on its dev realm file and on `keycloak-import --open-registration`.
- Remove the dry-run mode: `Program.cs:18,26,146,151,155`, `EtlContext.cs:15,27-35,43,101,116`, the rollback branch and wrong comment in `EtlRunner.cs:6-10,21-28`, and the dry-run header in `ConsoleReport.cs:9-12`.
- Replace the lenient argument handling (`Program.cs:15-29` ignores unknown options) with a strict parser. It runs before any configuration is read and knows which commands each environment allows:
  - All environments: `all` (guarded full run), `verify` (read-only), `migrate` (creates the schema before the single Production run; `app/mobile/e2e/write-stack.sh` uses it), `reset` (refuses outside Development and Staging), `media`, `media-verify` (read-only), `keycloak-import`.
  - Development only: the single write steps (`species`, `profiles`, `tanks`, ...). They skip the empty-target guard, run in their own transaction and print a warning line. They serve debugging and the TASK-3.33 regression check on the dev stack. In Staging and Production they are rejected with a message naming the environment.
- Empty-target guard at the start of `all`, before any write to the database or the object store. Today the object store is created and its bucket policy applied at `Program.cs:142,203`, so the guard moves in front of that. The guard:
  - fails when the schema has pending migrations;
  - fails when any base table in the EF-managed schemas `public` and `archive` (except `__EFMigrationsHistory`) holds a row, and names those tables;
  - takes a transaction-scoped advisory lock, so two runs started at once cannot both pass.
- Atomic full run: a new `EtlRunner.RunAllAsync` runs the guard and all write steps in one transaction. A failed run leaves the target empty and can be retried, including in Production, where the reset refuses. The verify step runs after the commit; a mismatch sets exit code 1 and rolls nothing back. `EtlRunner.RunAsync` (one transaction per step) stays for the step tests, the Development single steps and the standalone `verify`.
- New `reset` command in the ETL. It is a C# command, so the same code can later run as a staging job without docker.
  - Allowed environments: `Development` and `Staging` only, case-insensitive. Anything else is refused, including an unset `DOTNET_ENVIRONMENT`, because `Program.cs:51` treats unset as Production.
  - Preflight: get a Keycloak admin token and read the realm before anything is dropped.
  - Database: `SET lock_timeout`, drop the `archive` and `public` schemas, recreate `public` with the PostgreSQL defaults, then `MigrateAsync`.
  - Keycloak: delete all realm users except those with the realm role `admin`, and set `registrationAllowed=false`. Staging keeps registration closed (TASK-2.27); testers get accounts the operator creates.
  - The reset never builds an object store, so media objects stay by construction.
- bootstrap.sh (R38) resets on every run, in this order:
  1. Wait for Postgres and fail clearly if it never becomes ready.
  2. `etl reset`
  3. Delete and re-create the `cichlids` realm from the file.
  4. `etl all`
  5. `etl media`
  6. `etl keycloak-import --open-registration` (from TASK-2.27)
  7. `etl verify`

  The legacy stack directory default becomes relative to `REPO_ROOT`. The realm is re-created after the reset because the reset deletes users, and re-creating the realm brings back `dev-user` and `dev-mod`.
- Kept as they are, only their doc comments change: `PgUpsert`, `PgBatchUpsert`, `PgBulkInsert`, `PgChildRows`, and the stale-row and child-row deletes in the steps (Forum `:83-84,146-186`, Picture `:553,655`, SpeciesLinks `:85`).
  - Conflict handling is still needed inside one run: `PlaceholderProfiles.cs:39,60` is upserted from five steps.
  - The step tests rerun steps against shared fixtures.
  - In Staging and Production these paths only run inside the guarded full run.

### Steps

1. Strict command line, environment-aware command set, dry-run removal (about 0.5 day)
   - Failing tests first:
     - `tests/Cichlids.Etl.Tests/EtlCommandLineTests.cs`:
       - `all --dry-run` is rejected with "Unknown option '--dry-run'".
       - `profiles` is accepted in Development and rejected in Staging and Production with the environment named.
       - `reset` parses in every environment; refusing it is the reset policy's job.
       - Known options parse, and an option missing its value is an error.
     - `tests/Cichlids.Etl.Tests/EtlProcessTests.cs` spawns `dotnet Cichlids.Etl.dll all --dry-run` and expects exit 1 with that message. This is the end-to-end test for AC #4; it needs no database because parsing comes first.
   - Production change:
     - New `src/Cichlids.Etl/Runtime/EtlCommandLine.cs` taking the arguments and the environment name. Per-command options: `all`, `verify` and `media-verify` take `--out`; `media` takes `--limit`, `--workers` and `--missing-out`, plus `--force` until TASK-3.31 removes it; `migrate`, `reset` and single steps take none; `keycloak-import` takes `--open-registration` (from TASK-2.27).
     - `Program.cs` parses first and exits with 1, like the unknown-step path at `:134-136`.
     - Remove `DryRun` from `EtlContext.cs` and `EtlRunner.cs`, and the parameter from `ConsoleReport.Print`. Drop `dryRun: false` from the 13 call sites in `tests/Cichlids.Etl.Tests`.
2. Isolated empty test databases and the guard (about 1 day)
   - Failing tests first:
     - Add `CreateEmptyTargetDatabaseAsync()` to `tests/Cichlids.Etl.Tests/VerifyEtlFixture.cs`: `CREATE DATABASE t_<guid>` in the same container, migrate, return the connection string.
     - New `EmptyTargetGuardTests`:
       - A database with one `species` row aborts with a message naming `public.species`, and the rest of the database is still empty.
       - A database with a pending migration aborts and names it.
       - A second run while the advisory lock is held fails fast.
   - Production change: new `src/Cichlids.Etl/Runtime/EmptyTargetGuard.cs` (reads tables from `information_schema.tables` for the schemas in the EF model, `SELECT EXISTS` per table, `pg_try_advisory_xact_lock`). `Program.cs` runs it for `all` before `CreateObjectStoreAsync`.
3. Atomic full run (about 0.5 day)
   - Failing tests first:
     - `RunAllAsync` with `SpeciesCatalogStep` and a step that throws leaves every table empty.
     - The full-pipeline part of `VerifyStepTests.cs:13-60` runs through `RunAllAsync` on an isolated database with 0 mismatches.
     - An injected mismatch gives exit 1 and leaves the committed data in place.
   - Production change: `EtlRunner.RunAllAsync`, with the guard inside the same transaction; the verify step runs after the commit. `Program.cs:149-156` calls it for `all`.
4. Reset: database part and environment policy (about 0.5 to 1 day)
   - Failing tests first:
     - `ResetPolicyTests` (unit): Development and Staging are allowed; Production, empty, null and `Prod` are refused.
     - `TargetDatabaseResetTests` on an isolated database: after a reset, every table is empty, `archive` exists and no migration is pending. A reset with Production refuses and the rows stay.
   - Production change: new `src/Cichlids.Etl/Runtime/TargetDatabaseReset.cs`. It prints the host and database, sets `lock_timeout = '10s'`, then runs `DROP SCHEMA IF EXISTS archive CASCADE; DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public AUTHORIZATION pg_database_owner; GRANT USAGE ON SCHEMA public TO PUBLIC;` followed by `MigrateAsync`. New `reset` command in `Program.cs`.
5. Reset: Keycloak part (about 0.5 to 1 day)
   - Failing tests first: new `tests/Cichlids.Etl.Tests/ResetCommandTests.cs` with its own Postgres and Keycloak fixture (realm from `Fixtures/cichlids-realm.json`, as in `KeycloakImportFixture.cs:35-39`). It creates users, one with the realm role `admin`, and checks:
     - After a Development or Staging reset, only the admin user is left, `registrationAllowed` is false, and the master admin still gets a token.
     - With Production, all users stay.
     - With an unreachable Keycloak, no database schema is dropped.
   - Production change:
     - In `src/Cichlids.Etl/Identity/KeycloakAdminClient.cs`, add: listing user ids (paged like `:88-118`); `GET roles/admin/users`; `DELETE users/{id}` with bounded parallelism, where 404 counts as done; a realm update that sets `registrationAllowed=false`. All calls go through `SendAsync` at `:276`, so TASK-3.33's retry also covers them.
     - Move `Program.cs:110-117` into a shared `KeycloakAdminSettings.Resolve(configuration, environmentName)` used by `keycloak-import` and `reset`. The current fallback stays; TASK-3.33 removes it.
     - A missing realm logs a warning and counts as having no users.
6. bootstrap.sh (about 0.5 day)
   - Failing test first: `app/stack/bootstrap_test.py`.
     - It puts a fake `docker` on `PATH`: `up` succeeds, `pg_isready` fails.
     - With `CICHLIDS_POSTGRES_WAIT_SECONDS=2` it expects a non-zero exit, the message "Postgres did not become ready" and no "Postgres is up." line.
     - It also checks that the legacy stack default contains no absolute `/workspaces` path.
     - Add it to `.github/workflows/ci.yml` next to the other Python checks.
   - Production change in `app/stack/bootstrap.sh`:
     - `:55-62`: fail after the configurable wait.
     - `:15`: default `$REPO_ROOT/workspace-legacy/legacy-stack`.
     - `:91-129`: the order from the Approach. The `etl migrate` call is replaced by `etl reset`, the realm step always deletes and re-creates, the account import passes `--open-registration`, and the standalone verify moves to the end.
     - `:2-7`: header comment.
7. ADR and documentation (about 0.5 day)
   - New ADR "Legacy import runs once against an empty target". Take the next free ADR number at implementation time; do not hardcode it.
     - Content: guard, atomic run, reset only in Development and Staging, exactly one run in Production, later corrections through migrations, and staging refreshed per ADR-0018 after go-live.
     - Add a pointer from ADR-0019 item 4 ("idempotent and repeatable") to the new ADR.
     - Regenerate `docs/adr/index.md` with `python3 tools/generate_adr_index.py` and check it with `--check`.
   - `app/stack/README.md`:
     - `:50-56`: bootstrap resets on every run and re-creates the realm; describe the run order.
     - `:66`: new default for the legacy stack directory.
     - `:81,89-91,145-146`: replace the text about the realm being skipped.
     - Add: stop the API before a reset, run the reset in dev only through bootstrap (the write E2E stack needs `dev-user`), and use `docker compose up -d` to only start services.
   - `PROJECT-STATUS.md:331,410-414`.
   - Class comments in `Persistence/PgUpsert.cs:5-9`, `PgBatchUpsert.cs:5-9`, `PgBulkInsert.cs:5-11`, `PgChildRows.cs:5-10`, `Steps/IEtlStep.cs` and `Runtime/EtlContext.cs:12-17`.

Size: M/L, about 4 to 4.5 days in 7 commits, plus one operator session on the dev stack (ETL, account import and verify take about 30 to 60 minutes).

### Verification

- AC #1:
  - Automated: `EmptyTargetGuardTests` passes.
  - Dev stack: on the seeded stack, `cd app/api && DOTNET_ENVIRONMENT=Development ~/.dotnet/dotnet run --project src/Cichlids.Etl -c Release -- all` exits 1 and names the tables. `docker compose -f app/stack/compose.yaml exec -T postgres psql -U cichlids -d cichlids -c "select count(*) from profile"` shows the same count before and after.
- AC #2:
  - Automated: the atomic full run on an isolated database passes with 0 mismatches.
  - Dev stack: `app/stack/bootstrap.sh` exits 0. The import summary shows about 12,050 created and about 6,447 profile links. The final `etl verify` shows 0 mismatches.
  - Media survives the reset: the object count from `docker run --rm --network host -e AWS_ACCESS_KEY_ID=cichlids -e AWS_SECRET_ACCESS_KEY=cichlids-dev-secret amazon/aws-cli --endpoint-url http://127.0.0.1:9000 s3 ls s3://cichlids-media --recursive --summarize` is the same before and after.
- AC #3:
  - Automated: `ResetPolicyTests` and `ResetCommandTests` pass.
  - Dev stack: `DOTNET_ENVIRONMENT=Production ... -- reset` exits 1 with the refusal, and so does a run with the variable unset. Row counts and Keycloak user counts are unchanged.
- AC #4:
  - Automated: `EtlCommandLineTests` and `EtlProcessTests` pass.
  - Dev stack: `... -- all --dry-run` prints "Unknown option '--dry-run'".
- Commands per environment: `DOTNET_ENVIRONMENT=Staging ... -- profiles` is rejected with the environment named. `DOTNET_ENVIRONMENT=Development ... -- profiles` runs on the seeded dev stack and prints the guard-skip warning.
- R38 and realm:
  - Automated: `bootstrap_test.py` passes.
  - Dev stack: the realm id from `kcadm.sh get realms/cichlids --fields id` differs before and after `bootstrap.sh`, and self-registration is open only after the account import step.

### Risks

- `DROP SCHEMA public` needs the database owner role. TASK-2.4 must give the staging ETL job that role.
- An API holding locks makes the reset wait. `lock_timeout` turns that into a clear failure; the README tells operators to stop the API first.
- One transaction over several million rows is fine for Postgres (the comment step already runs about 1.6 million rows in one), but WAL grows during the run.
- Deleting about 12k Keycloak users takes minutes and can hit 429 responses until TASK-3.33 adds retry with backoff.
- Development single steps skip the guard and can overwrite dev data. That is intended; they are rejected outside Development.
- bootstrap resets on every run, so data created by hand in the dev database is lost. The write E2E stack uses its own database and is not affected.
- Depends on TASK-2.27 (realm file and `--open-registration`). If it slips, step 6 must wait.
<!-- SECTION:PLAN:END -->
