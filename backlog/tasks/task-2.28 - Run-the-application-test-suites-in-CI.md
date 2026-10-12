---
id: TASK-2.28
title: Run the application test suites in CI
status: In Review
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-12 07:09'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
  - .github/workflows/ci.yml
parent_task_id: TASK-2
priority: high
ordinal: 59000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R6.

CI gets three application jobs: backend (.NET build and all tests with Testcontainers), frontend (typecheck and vitest for client-core and mobile) and the Playwright write tests against the compose stack without legacy data. The seeded smoke follows with TASK-2.24, the Maestro flows with TASK-2.25. These jobs are the backend, client-core and e2e checks named in TASK-2.11.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a push to main, When GitHub Actions runs, Then the backend, frontend and e2e write jobs run and the operator sees them green
- [ ] #2 Given a commit that breaks an API integration test, When GitHub Actions runs, Then the backend job fails
- [ ] #3 Given a commit that breaks the upload user path, When GitHub Actions runs, Then the e2e job fails
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

- Add three jobs to `.github/workflows/ci.yml`, replacing the placeholder comment at lines 96-102. Job ids match TASK-2.11: `backend`, `client-core` (display name "Frontend typecheck and unit tests (client-core, mobile)") and `e2e`.
- All three jobs run on every push to main and on every pull request, without path filters. The repository is public, so Actions minutes cost nothing. A required check with a path filter can stay pending (TASK-2.11).
- The backend needs no exclusions. Every test project is self-contained through Testcontainers:
  - Images: `postgres:17`, `mysql:5.7`, `rustfs/rustfs:1.0.0-beta.8`, `quay.io/keycloak/keycloak:26.6.4` (for example `EtlFixture.cs:57,63,76`, `KeycloakImportFixture.cs:35`).
  - The legacy MySQL container is seeded from `tests/Cichlids.Etl.Tests/Fixtures/*.sql` (60 KB).
  - Media tests use temp directories (`MediaTestSupport.cs:19`).
  - No test reads `/workspaces/legacy-data` or the dev database. So `backend` runs all three test projects of `app/api/Cichlids.sln`.
- The e2e job reuses the local entry point `pnpm run e2e:write` (`app/mobile/package.json`), which runs `e2e/write-stack.sh`.
  - That script needs Postgres, RustFS and Keycloak with the `cichlids` realm running (`write-stack.sh:49-65`). It needs no legacy data.
  - ETL `migrate` does not touch MySQL (`Cichlids.Etl/Program.cs:60-70`).
  - Keycloak imports the realm, including `dev-user`, at first start (`compose.yaml:31,39`).
  - `bootstrap.sh` cannot run in CI because it exits without legacy MySQL (`bootstrap.sh:72-89`). A new `app/stack/services-up.sh` starts only the three services and waits for them. `bootstrap.sh` step 1 calls it.
- Use the same toolchain as the devcontainer:
  - .NET from `tools/install-dotnet.sh`, installed to `~/.dotnet`. That is the default path in `write-stack.sh:20` and `bootstrap.sh:18`. The job checks it with `tools/verify-toolchain.sh backend`.
  - Node 24 via `actions/setup-node@v5`.
  - pnpm pinned through `"packageManager": "pnpm@12.4.2"` in `app/package.json`.
- Action versions use major tags, as in the existing jobs. `pnpm/action-setup`, `actions/cache` and `actions/upload-artifact` get their current major tag, checked when implementing.
- Known defect fixed in this task: NuGet audit reports NU1903 for SSH.NET 2025.1.0, which comes in through Testcontainers 4.13.0 (GHSA-q939-rpr3-3284, GHSA-mggc-4xg6-vcxf). This task removes the vulnerable version, and audit warnings NU1901 to NU1904 then fail the build.
- Recorded as still missing (R6): the seeded Playwright smoke (TASK-2.24, depends on TASK-2.17), the Maestro flows (TASK-2.25), and a lint step (no lint script exists).

### Steps

1. `client-core` job (frontend typecheck and vitest)
   - Change:
     - Add `"packageManager": "pnpm@12.4.2"` to `app/package.json`.
     - Job steps: checkout; `pnpm/action-setup` with `package_json_file: app/package.json`; setup-node 24 with `cache: pnpm` and `cache-dependency-path: app/pnpm-lock.yaml`.
     - In `app/`: `pnpm install --frozen-lockfile`.
     - `pnpm --filter @cichlids/client-core build`. This step is explicit because mobile resolves the client-core types from `dist/`, which is not checked in.
     - `pnpm -r typecheck` and `pnpm -r test`. `timeout-minutes: 10`.
   - Red first:
     - Locally, add a type error in `app/mobile/src/upload/uploadForm.ts` and a failing assertion in `app/client-core/src/client.test.ts`. Both commands exit non-zero.
     - On the PR branch, push the job together with that break commit: the job is red. Drop the commit: the job is green.
   - Files: `ci.yml`, `app/package.json`.
2. Fix NU1903 and make NuGet audit warnings fail the build
   - Red first: `dotnet build app/api/Cichlids.sln -c Release -warnaserror` fails today with six NU1903 errors for SSH.NET 2025.1.0. All three test projects are affected.
   - Change:
     - Check whether a Testcontainers release later than 4.13.0 pulls in a patched SSH.NET. If yes, bump `Testcontainers`, `Testcontainers.PostgreSql`, `Testcontainers.MySql` and `Testcontainers.Keycloak` in all three test `.csproj` files to that version.
     - If no such release exists, add a direct `PackageReference` to the patched SSH.NET version in the three test projects. Add a short comment naming the GHSA ids.
     - Create `app/api/Directory.Build.props` with `<NuGetAudit>true</NuGetAudit>`, `<NuGetAuditMode>all</NuGetAuditMode>` and `<WarningsAsErrors>$(WarningsAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsAsErrors>`. This turns any future vulnerable dependency into a red backend job.
   - Green: `dotnet build app/api/Cichlids.sln -c Release` passes with zero NU190x warnings. `dotnet test` still passes locally.
   - Files: `app/api/Directory.Build.props` (new), `app/api/tests/*/*.csproj`.
3. `backend` job (.NET build and all tests with Testcontainers)
   - Change:
     - Checkout; `bash tools/install-dotnet.sh`; add `$HOME/.dotnet` to `GITHUB_PATH`; set `DOTNET_ROOT` and `DOTNET_CLI_TELEMETRY_OPTOUT=1`; run `tools/verify-toolchain.sh backend`.
     - Cache `~/.nuget/packages`, keyed on `hashFiles('app/api/**/*.csproj', 'app/api/Directory.Build.props')`.
     - `dotnet build app/api/Cichlids.sln -c Release`. The audit errors from step 2 apply here.
     - `dotnet test app/api/Cichlids.sln -c Release --no-build --logger trx --logger "console;verbosity=normal" --results-directory TestResults --blame-hang-timeout 10m`.
     - Upload `TestResults` as an artifact only when the job fails. `timeout-minutes: 30`.
   - Red first:
     - Locally, remove `app.MapLegacyRedirectsEndpoints();` (`app/api/src/Cichlids.Api/Program.cs:119`). `LegacyRedirectsEndpointsTests` go red against the Postgres test container.
     - Repeat the same break on the PR branch: `backend` is red. Revert: green.
   - Files: `ci.yml`.
4. Make the API tests independent of the local dev stack (only if the first CI run shows it is needed)
   - Suspect: `tests/Cichlids.Api.Tests/HealthzTests.cs:6` uses a bare `WebApplicationFactory<Program>`. The host starts in Development with `OutboxDispatcherHostedService` enabled (`Program.cs:47`, `OutboxDispatcherOptions.cs:14`). It polls `127.0.0.1:5432` from `appsettings.Development.json`.
     - Locally it talks to the dev database.
     - In CI there is no Postgres there, and `RunOnceAsync` has no catch.
   - Red first: run `dotnet test --filter FullyQualifiedName~HealthzTests` with the dev stack stopped, or read the step 3 CI log for a failure or a logged BackgroundService crash.
   - Change: apply the `OutboxDispatcher:Enabled=false` override that `ApiFixture.cs:64` uses, via `WithWebHostBuilder`.
   - Files: `HealthzTests.cs`.
5. `app/stack/services-up.sh` (stack start without legacy data)
   - Change:
     - New script: `docker compose -f compose.yaml up -d postgres rustfs keycloak`.
     - Then the waits from `bootstrap.sh:55-70`: Postgres `pg_isready`, a TCP check for RustFS on 9000, an HTTP check for the Keycloak master realm.
     - An optional flag also waits for `http://localhost:8180/realms/cichlids`. `write-stack.sh:61` checks that realm without retrying. `bootstrap.sh` does not use the flag, because it imports the realm later (step 5) on older volumes.
     - Replace `bootstrap.sh` step 1 (lines 51-70) with a call to the script. Document it in `app/stack/README.md`.
   - Red first: on a host without legacy MySQL (a CI runner), `bootstrap.sh` exits 1 at line 88. So CI cannot use it.
   - Green:
     - `services-up.sh` comes up on a throwaway compose project (`COMPOSE_PROJECT_NAME=ci-probe`, removed afterwards with `down -v` on that project only).
     - A rerun of `bootstrap.sh` on the dev stack is still idempotent and green.
   - Files: `app/stack/services-up.sh` (new), `app/stack/bootstrap.sh`, `app/stack/README.md`.
6. `e2e` job (Playwright write tests against the compose stack)
   - Change:
     - Checkout, then `app/stack/services-up.sh` with the realm wait, early, so Keycloak boots while the rest installs.
     - `tools/install-dotnet.sh` plus `GITHUB_PATH`; the NuGet cache; pnpm and setup-node as in step 1; `pnpm install --frozen-lockfile`; `pnpm exec playwright install --with-deps chromium` in `app/mobile`.
     - `pnpm --dir app/mobile run e2e:write` with `EXPO_NO_TELEMETRY=1`.
     - Only when the job fails, upload `/tmp/cichlids-write-stack/api.log`, `web.log` and the output of `docker compose -f app/stack/compose.yaml logs`.
     - Always run `docker compose -f app/stack/compose.yaml down -v` at the end. `timeout-minutes: 30`.
   - Red first:
     - Locally, make publish do nothing in `app/mobile/app/upload/index.tsx:278` (`onPress={() => undefined}`). `pnpm run e2e:write` fails in `upload.write.mjs` at the gallery detail `waitForURL` (line 182).
     - This also shows the exit code gets through `write-stack.sh:209` (`trap cmd_down EXIT` with `set -e`).
     - Repeat on the PR branch: `e2e` red, `backend` green. Revert: green.
   - Files: `ci.yml`.
7. Update the comment in `ci.yml` and the task record
   - Change:
     - Replace the placeholder at `ci.yml:96-102` with a short block. It lists what is not in CI yet (seeded smoke TASK-2.24, Maestro TASK-2.25, lint). It states that `e2e` checks the backend toolchain scope, not the Android scope.
     - Add the back-tag `# Story: task-2.28`. Add `.github/workflows/ci.yml` to the task references.
     - Record the run URLs for AC #1 to #3 in the Implementation Notes.
   - Check: `node tools/check-story-test-binding.mjs` stays green once the task is Done.
   - Files: `ci.yml`, task metadata through the backlog CLI or MCP.
8. Before every commit, run `python3 tools/check-no-plaintext-secrets.py`. New files contain no credentials. Dev credentials stay in `compose.yaml` and `support.mjs:39-40`, which already carry `gitleaks:allow`.

### Verification

- AC #1 (push to main, three jobs green)
  - The operator pushes main. Local main is ahead of `origin/main`, and pushing is the operator's job.
  - On GitHub: Actions, workflow "CI", the run for the pushed commit. `backend`, `client-core` and `e2e` are green.
  - CLI alternative: `gh run list --workflow ci.yml --branch main`, then `gh run view <id>`. Record the run URL and the job durations in the notes.
- AC #2 (a broken API integration test fails the backend job)
  - The operator creates `ci-proof/backend-red` with only `Program.cs:119` removed, pushes it and opens a draft PR. The workflow runs on pull requests and on pushes to main only.
  - Expected: `backend` red, with `LegacyRedirectsEndpointsTests` named in the log. `client-core` and `e2e` green.
  - Close the PR, delete the branch, record the run URL.
- AC #3 (a broken upload path fails the e2e job)
  - Same procedure with `ci-proof/upload-red` and the publish no-op in `upload/index.tsx:278`.
  - Expected: `e2e` red. The log shows `Running upload.write.mjs...` and then the timeout on the gallery detail URL. `backend` green.
  - Close the PR, delete the branch, record the run URL.
- NuGet audit: the step 3 build log of the AC #1 run shows no NU190x warning. The step 2 red proof is recorded in the notes.

### Risks

- Runtime: backend is estimated at 8 to 15 minutes, because several fixtures start their own MySQL, Postgres, RustFS and Keycloak containers. e2e is estimated at 10 to 15 minutes: Keycloak boot, building the ETL and the API in Release, the `bucket-tool.cs` restore, `expo export`, three specs. Mitigations: start the services first, cache NuGet and the pnpm store. Split backend into a matrix per test project only if a run takes more than 15 minutes.
- Flakiness: Keycloak start time; the 20-second popup timeouts in `support.mjs:55-59`; RustFS is a beta image. The specs have no traces. Playwright tracing is a follow-up if the job turns flaky.
- Docker in Actions: `ubuntu-latest` ships Docker and Compose v2, and Testcontainers with Ryuk works there. `mysql:5.7` is amd64 only, which the runners are. Docker Hub pull limits are unlikely on hosted runners. The fallback is a Docker Hub login secret or a registry mirror.
- NuGet audit as errors: a newly published advisory can turn backend red without any code change. That is intended and gets fixed when it happens. Audit needs network access to nuget.org during restore.
- Hidden local dependencies: tests may pass locally only because the dev stack is running (HealthzTests, step 4). The first CI run is the real check.
- Toolchain drift: pnpm was unpinned before step 1. The e2e job needs `~/.dotnet`, or `DOTNET` set explicitly.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Done
- Added the jobs `backend`, `client-core` and `e2e` to `.github/workflows/ci.yml` (no path filters, push to main and pull requests).
- Pinned pnpm via `packageManager` in `app/package.json`; pnpm 12 added its own entry to `app/pnpm-lock.yaml`.
- Bumped Testcontainers to 4.14.0 in the three test projects (pulls SSH.NET 2026.0.0, fixes NU1903). Added `app/api/Directory.Build.props` that turns NU1901 to NU1904 into errors.
- Added `app/stack/services-up.sh` (optional `--wait-realm`), used by `bootstrap.sh` step 1 and by the e2e job.
- HealthzTests passes without Postgres on 127.0.0.1:5432, so no change was made there.

### Local proofs
- client-core: type error in `uploadForm.ts` and a failing assertion in `client.test.ts` both exit non-zero; reverted.
- NuGet audit: `dotnet build -warnaserror` failed with six NU1903 before the bump, builds clean after.
- backend: without `MapLegacyRedirectsEndpoints` the LegacyRedirectsEndpointsTests fail; full suite green (113 + 189 + 185).
- e2e: publish as no-op fails in upload.write.mjs at waitForURL (exit 1); unbroken run passes all 3 specs.
- services-up.sh verified on a throwaway compose project (ci-probe), removed afterwards. bootstrap.sh was not rerun against the dev stack.

### Operator steps still needed
- AC #1: push main and confirm backend, client-core and e2e are green (`gh run list --workflow ci.yml --branch main`); record URL and durations.
- AC #2: branch ci-proof/backend-red with only the `app.MapLegacyRedirectsEndpoints();` line removed from Program.cs, draft PR; expect backend red only.
- AC #3: branch ci-proof/upload-red with `onPress={() => undefined}` at app/mobile/app/upload/index.tsx:278; expect e2e red only.
- Record the run URLs here, then check the ACs.
<!-- SECTION:NOTES:END -->
