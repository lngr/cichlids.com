---
id: TASK-2.27
title: Separate the Keycloak realm configuration for staging and production
status: In Progress
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-12 07:34'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
  - tools/check-keycloak-realm.py
  - app/api/tests/Cichlids.Api.Tests/AuthenticationSettingsTests.cs
  - app/api/tests/Cichlids.Api.Tests/StagingAuthenticationStartupTests.cs
  - app/api/tests/Cichlids.Etl.Tests/DeployedRealmImportTests.cs
  - app/api/tests/Cichlids.Etl.Tests/KeycloakAccountImportTests.cs
  - app/api/tests/Cichlids.Etl.Tests/KeycloakRegistrationGuardTests.cs
parent_task_id: TASK-2
priority: high
ordinal: 56000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R7, R33.

The dev realm file stays and is marked as local only. Staging and production get their own realm configuration: HTTPS required, registration opened only after the account import (ADR-0023), no password grant on the public client, exact redirect URIs per environment, no test accounts. A CI check fails when a dev setting appears in that configuration. Outside Development the API refuses to start without an HTTPS authority and an audience.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Given the staging realm configuration, When the operator imports it into a fresh Keycloak, Then HTTPS is required, registration is closed, the public client has no password grant and no test account exists
- [ ] #2 Given a commit that copies a dev setting into the staging configuration, When GitHub Actions runs, Then the check fails
- [x] #3 Given the API in the Staging environment without Authentication:Authority, When the operator starts it, Then it refuses to start and names the setting
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

#### Realm files: one per environment, no templating
- `app/stack/keycloak/cichlids-realm.json` stays in place and is marked local only.
  - compose mounts the whole folder for `--import-realm` (`app/stack/compose.yaml:31,39`).
  - `app/stack/bootstrap.sh:109-121` imports it.
  - The ETL tests link it (`app/api/tests/Cichlids.Etl.Tests/Cichlids.Etl.Tests.csproj:38`).
  - Deployed realm files must not go into that folder, because Keycloak imports every JSON file there.
- New files, following the ADR-0010 layout `apps/<service>/overlays/{staging,prod}`:
  - `apps/keycloak/overlays/staging/cichlids-realm.json`
  - `apps/keycloak/overlays/prod/cichlids-realm.json`
- Why separate files and not a template:
  - What reviewers read is exactly what Keycloak imports.
  - The CI check reads final values without a render step.
  - Only `displayName` and the `redirectUris`/`webOrigins` of `cichlids-app` may differ.
  - A parity rule in the check catches drift between the two files.
- Both files are plain Keycloak realm JSON, readable by `--import-realm` and by keycloak-config-cli. The Keycloak deployment task picks the mechanism; this task records the constraints it must meet.

#### Hostnames
- prod: `auth.cichlids.com`, `api.cichlids.com`, web `cichlids.com`.
- staging: `auth.staging.cichlids.com`, `api.staging.cichlids.com`, web `staging.cichlids.com`.

#### Public client `cichlids-app` (PKCE S256, no password grant, no implicit flow)
- staging: `redirectUris` `cichlids://auth` and `https://staging.cichlids.com/auth`, `webOrigins` `https://staging.cichlids.com`, so the MVP can be tested in a browser on staging.
- prod: `redirectUris` `cichlids://auth` only, `webOrigins` empty.
- `cichlids://auth` is what the app builds with `makeRedirectUri` (`app/mobile/src/auth/config.ts:19-20`, `app/mobile/src/auth/AuthProvider.tsx:46-48`).
- TASK-3.41 later replaces web sign-in in both environments with the cookie-based BFF.

#### Registration
- Neither deployed file sets `registrationAllowed`. Keycloak creates a realm with registration closed by default.
- The CI check rejects the key in both files.
- `etl keycloak-import --open-registration` opens registration only after a run with no unresolved account.
- Who uses it:
  - dev bootstrap uses the option;
  - production go-live uses it once;
  - staging never uses it: the staging reset and import run without it, and testers get operator-created accounts.
- Why: the order "import first, then open registration" is enforced by code. Re-applying the realm from Git cannot close registration after go-live, because the key is not in the file.

#### Hardening
- `sslRequired` is `all`, not `external`. Keycloak runs behind ingress-nginx; with `external`, broken proxy headers would quietly allow HTTP from private addresses.
- `verifyEmail` true, `bruteForceProtected` true, `duplicateEmailsAllowed` false.
- Google and Facebook stay disabled with the `not-configured` placeholder (ADR-0022, DRAFT-7).
- No users in the files. Admin accounts are created outside Git.

#### CI check
- `tools/check-keycloak-realm.py`, Python standard library only, with `--self-test`. Same style as `tools/check-project-separation.py`.

#### API startup check (R33)
- `AuthenticationSettings.Resolve(configuration, environmentName)`, built like `app/api/src/Cichlids.Infrastructure/Slugs/SlugSecretResolver.cs:30-55`.

#### Size
- M. Seven commits, about 1.5 to 2 days.

### Steps

#### 1. API refuses to start without valid auth settings (R33, AC #3)
- Red: `app/api/tests/Cichlids.Api.Tests/AuthenticationSettingsTests.cs` (pure unit tests):
  - In Staging, a missing or blank Authority names `Authentication:Authority`.
  - An `http://` or relative Authority names `Authentication:Authority` and HTTPS.
  - A blank Audience names `Authentication:Audience`. The test overrides it with an empty string, because `appsettings.json` sets it.
  - `RequireHttpsMetadata=false` outside Development names that setting.
  - Development still accepts `http://localhost:8180/realms/cichlids` with `RequireHttpsMetadata=false`.
- Red: `app/api/tests/Cichlids.Api.Tests/StagingAuthenticationStartupTests.cs`, modelled on `ProductionSlugSecretTests.cs:14-52`:
  - `UseEnvironment("Staging")`, `Slugs:Secret` set, no Authority.
  - `factory.Server` throws, and the message contains `Authentication:Authority`.
  - Tagged `Story: task-2.27`.
- Green:
  - New `app/api/src/Cichlids.Api/Features/Auth/AuthenticationSettings.cs`: a record plus `Resolve`.
  - `app/api/src/Cichlids.Api/Program.cs:49-64` uses the resolved settings. The call sits before `AddAuthentication`, so before the slug check at `Program.cs:88`.
- Remove `ProductionSlugSecretTests.cs:39` (`RequireHttpsMetadata=false`). Otherwise that test fails on the new auth check instead of the slug secret.
- Commit: `feat(api): refuse to start outside Development without an HTTPS authority and an audience`.

#### 2. Realm check with self-test (red against the repository)
- New `tools/check-keycloak-realm.py`, tagged `Story: task-2.27`.
- Rules for each staging and prod file:
  - The file exists.
  - `sslRequired` is `all`.
  - No `registrationAllowed` key.
  - No `users` key. No `credentials` or `federatedIdentities` anywhere.
  - Every client sets `directAccessGrantsEnabled` explicitly to false.
  - No client has `implicitFlowEnabled` true.
  - Public clients have `pkce.code.challenge.method` S256.
  - `redirectUris` contain no `*`; each is `https://...` or exactly `cichlids://auth`.
  - `webOrigins` are exact `https://host` values without `+` or `*`. The list may be empty.
  - `verifyEmail` and `bruteForceProtected` are true; `duplicateEmailsAllowed` is not true.
  - None of these strings appear anywhere: `localhost`, `127.0.0.1`, a `.test` domain, `dev-password`, `dev-user`, `dev-mod`, `exp://`, `local development only`.
  - Parity: staging equals prod after removing `displayName` and the `redirectUris`/`webOrigins` of `cichlids-app`.
- Rule for the dev file: its `displayName` contains `local development only`.
- `--self-test`:
  - A good realm passes.
  - Each single dev setting copied in fails: `sslRequired` none, `registrationAllowed` true, password grant, wildcard redirect, localhost redirect, `+` origin, a user with a password, a `.test` email.
  - The real dev file, checked as if it were the staging file, fails.
- CI: new job `keycloak-realm` in `.github/workflows/ci.yml`, after `secrets-scan` (`ci.yml:47-62`). It runs `--self-test`, then the check.
- Commit on the same branch as step 4, so `main` never sees a red check.

#### 3. Fresh-import test for the staging realm (AC #1, red)
- New `app/api/tests/Cichlids.Etl.Tests/DeployedRealmImportTests.cs`, with a fixture that starts only Keycloak 26.6.4 using `WithRealm(...)`, like `KeycloakImportFixture.cs:35-39`.
- Link the staging file in the csproj next to line 38.
- Checks through the admin API:
  - `sslRequired` is `all`; registration is closed; `verifyEmail` and `bruteForceProtected` are on.
  - `cichlids-app` has the password grant off and exactly the staging redirect URIs and web origin.
  - `users/count` is 0.
- Behaviour check: a plain-HTTP request to `/realms/cichlids/protocol/openid-connect/auth` gets the "HTTPS required" answer.
- Tagged `Story: task-2.27`.
- Prod is covered by the static check and its parity rule, which saves one container start.

#### 4. Staging and prod realm files (steps 2 and 3 turn green)
- Start from the dev file and change:
  - `sslRequired` from none (dev line 4) to `all`;
  - drop `registrationAllowed` (dev line 5);
  - add `verifyEmail`, `bruteForceProtected` and `duplicateEmailsAllowed` false;
  - on `cichlids-app`: password grant off (dev line 212), per-environment redirect URIs and web origins as in the Approach (dev lines 214-215), `implicitFlowEnabled` false;
  - remove the users (dev lines 229-250).
- Keep the realm name, required actions, roles, client scopes, the `cichlids-api` client and the disabled identity providers.
- `displayName`: `cichlids` for prod, `cichlids staging` for staging.
- Commit: `feat(keycloak): add the staging and production realm configuration`.

#### 5. Registration opens only after the account import
- Red: in the `Scenario` of `app/api/tests/Cichlids.Etl.Tests/KeycloakAccountImportTests.cs` (around lines 369-405):
  - the first run imports without the option, the second with it;
  - record `registrationAllowed` after each run;
  - tests assert closed after the first run and open after the second.
- This needs the dev realm to start closed: `cichlids-realm.json:5` becomes false.
- Green:
  - `KeycloakAdminClient.OpenRegistrationAsync` sends `PUT admin/realms/{realm}` with `registrationAllowed` true.
  - `KeycloakAccountImportCommand.ImportAsync` and `RunAsync` get an `openRegistration` parameter. Registration opens only when `Unresolved` is 0 (see `KeycloakAccountImportCommand.cs:68-73`).
  - `app/api/src/Cichlids.Etl/Program.cs:106-122` parses `--open-registration`; the usage text at lines 17-21 mentions it.
- `bootstrap.sh:123-129` runs `etl keycloak-import --open-registration`. Without an Auth0 export it prints that registration stays closed.
- Commit: `feat(etl): open self-registration only after a complete account import`.

#### 6. Mark the dev realm as local only
- Red: the dev-file rule from step 2 fails.
- Green:
  - `cichlids-realm.json` gets `displayName` `cichlids (local development only)`, visible on the dev login page.
  - `app/stack/README.md` (around lines 119-160) gets a "local only, never deploy" section. It explains when registration opens and gives a kcadm command for existing dev volumes whose realm still has registration open.
- What the Playwright write tests depend on:
  - They sign in as `dev-user` from the realm file (`app/mobile/e2e/playwright/support.mjs:12-13`).
  - `comment.write.mjs:20` uses the password grant on the dev realm.
  - None uses self-registration, and the dev realm keeps the password grant and its dev users. Closing dev registration therefore needs no e2e change.
- Check: run `pnpm run e2e:write` against a freshly reset dev stack once.
- Commit: `docs(stack): mark the dev realm as local only`.

#### 7. Docs and ADR
- New `apps/keycloak/README.md`:
  - which file is for which environment, and the rules the check enforces;
  - how an operator checks a fresh import;
  - creating admin and tester accounts outside Git with kcadm;
  - production go-live order: realm, then account import with `--open-registration`, then admin accounts;
  - staging: registration stays closed.
  - Constraints for the Keycloak deployment:
    - `KC_PROXY_HEADERS=xforwarded` and an https `KC_HOSTNAME`;
    - SMTP from a SOPS secret (TASK-2.6);
    - identity provider secrets per DRAFT-7;
    - never add `registrationAllowed` to the file.
- New ADR under `docs/adr/` with the next free number at implementation time, titled "Keycloak realm configuration per environment". It refines ADR-0010: one file per environment guarded by CI, and registration state is runtime state that the import opens.
- Regenerate the ADR index with `python3 tools/generate_adr_index.py`; CI checks it at `ci.yml:10-19`.
- Add the test files and the check script to the TASK-2.27 references, so the story-to-test binding finds the `task-2.27` tags.

### Verification

#### AC #1: a fresh import of the staging realm
- Automated: `DeployedRealmImportTests`, run with `dotnet test app/api`.
- Operator check:
  - `docker run -d --name kc-check -p 18080:8080 -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin -v "$PWD/apps/keycloak/overlays/staging:/opt/keycloak/data/import:ro" quay.io/keycloak/keycloak:26.6.4 start-dev --import-realm`
  - `docker exec kc-check /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password admin`
  - `kcadm.sh get realms/cichlids --fields sslRequired,registrationAllowed` shows `all` and false.
  - `kcadm.sh get clients -r cichlids -q clientId=cichlids-app --fields directAccessGrantsEnabled,redirectUris,webOrigins` shows false and the staging URIs.
  - `kcadm.sh get users/count -r cichlids` shows 0.
  - `curl -s http://localhost:18080/realms/cichlids/account` shows "HTTPS required".
  - `docker rm -f kc-check`

#### AC #2: a dev setting copied into staging fails CI
- Automated: the `--self-test` cases in the `keycloak-realm` job.
- Operator check:
  1. Open a throwaway PR that sets `sslRequired` to none in the staging file.
  2. The `keycloak-realm` job fails and names the file and the rule.
  3. Close the PR unmerged.

#### AC #3: the API refuses to start in Staging without an Authority
- Automated: `StagingAuthenticationStartupTests` and `AuthenticationSettingsTests`.
- Operator check:
  - In `app/api/src/Cichlids.Api`, run `ASPNETCORE_ENVIRONMENT=Staging CICHLIDS_SLUG_SECRET=operator-check ~/.dotnet/dotnet run --no-launch-profile`. The API exits and names `Authentication:Authority`.
  - With `Authentication__Authority=http://kc.example`, the message names HTTPS.

#### Registration after the import (R7)
- The scenario tests from step 5.
- Operator check: on a reset dev stack, `bootstrap.sh` shows `registrationAllowed` true only after the import step.

#### Definition of Done
- `dotnet test app/api` and the Python checks are green.
- Full CI is green. The .NET suites only run in CI once TASK-2.28 lands.

### Risks
- **`sslRequired` `all` behind the ingress.** Wrong proxy headers make every login fail with "HTTPS required". This is intended; the Keycloak deployment must follow the README constraints.
- **Admin API against a realm with `sslRequired` `all` over HTTP in the container test.** It should work because admin authentication happens in the master realm. Confirm in the red phase of step 3; the fallback is to read the realm with kcadm inside the container.
- **keycloak-config-cli and missing keys.** It is not verified that it leaves a missing top-level key alone. If it resets it, production re-runs the idempotent `keycloak-import --open-registration` after a realm apply.
- **No outgoing mail yet.** `verifyEmail` and password resets for imported members need SMTP, which comes with the Keycloak deployment task.
- **The client scope list is explicit,** so built-in scopes such as `offline_access` are missing. TASK-3.41 must add them.
- **Existing dev volumes keep the old realm** with registration open until a reset or the kcadm command from the README.
- **`app/` and `apps/` are easy to mix up.**
- **Preview environments (`pr-<n>.dev.cichlids.com`) are out of scope.**

### Order
- Do this before TASK-3.30. That task's dev reset re-imports the dev realm and runs `keycloak-import --open-registration`. Its staging reset imports the staging file and runs the import without the option. Both tasks touch `bootstrap.sh` and ETL `Program.cs`.
- Do this before TASK-3.41. That task changes all three realm files (offline sessions, the `offline_access` scope, the BFF client with a secret that is never stored in Git) and extends the check for confidential clients.
- This is already a dependency of TASK-2.4, with no file overlap. The realm files wait for the Keycloak deployment task.
- TASK-2.28 should land first or in parallel so the .NET tests run in CI.
- TASK-3.33 also touches `KeycloakAdminClient.cs` and ETL `Program.cs`; expect a small rebase.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation
- API: `AuthenticationSettings.Resolve(configuration, environmentName)` (`app/api/src/Cichlids.Api/Features/Auth/AuthenticationSettings.cs`) runs in `Program.cs` before `AddAuthentication`. Outside Development it rejects a missing or blank `Authentication:Authority`, a non-HTTPS or relative authority, a blank `Authentication:Audience` and `Authentication:RequireHttpsMetadata=false`, and names the key in the message.
- Realm files: `apps/keycloak/overlays/staging/cichlids-realm.json` and `apps/keycloak/overlays/prod/cichlids-realm.json`. `sslRequired` all, no `registrationAllowed`, no users, `verifyEmail` and `bruteForceProtected` on, `cichlids-app` without password grant and implicit flow, exact redirect URIs per environment (staging adds `https://staging.cichlids.com/auth` and the web origin `https://staging.cichlids.com`).
- CI: `tools/check-keycloak-realm.py` (standard library only, `--self-test` with 35 cases) and the `keycloak-realm` job in `.github/workflows/ci.yml`. It also requires the dev realm display name to contain `local development only`.
- Registration: `etl keycloak-import --open-registration` opens self-registration via `KeycloakAdminClient.OpenRegistrationAsync` only when no account is unresolved. The dev realm starts with registration closed; `bootstrap.sh` passes the option.
- Docs: `apps/keycloak/README.md`, ADR-0025, section "Local only, never deploy" in `app/stack/README.md`.

### Tests run
- `AuthenticationSettingsTests`, `StagingAuthenticationStartupTests`, `ProductionSlugSecretTests` (serialized in one xunit collection because they set process environment variables).
- `DeployedRealmImportTests` (fresh Keycloak 26.6.4 import of the staging file) and the extended `KeycloakAccountImportTests` scenario (registration closed after the first run, open after the second).
- `python3 tools/check-keycloak-realm.py --self-test` and the check itself; `python3 tools/generate_adr_index.py --check`.
- Full `dotnet test app/api/Cichlids.sln` green. `pnpm --dir app/mobile run e2e:write` green on a throwaway compose project with a fresh dev realm (closed registration).
- AC #2 needs a GitHub Actions run and stays unchecked.

### Operator checks
- AC #1: run the commands in `apps/keycloak/README.md` section "Check a fresh import" against the staging file. Expect `sslRequired` all, registration false, no password grant, staging URIs, 0 users, "HTTPS required" over plain HTTP.
- AC #2: open a throwaway PR that sets `sslRequired` to none in the staging file; the `keycloak-realm` job must fail naming the file and rule. Close the PR unmerged.
- AC #3: in `app/api/src/Cichlids.Api` run `ASPNETCORE_ENVIRONMENT=Staging CICHLIDS_SLUG_SECRET=operator-check ~/.dotnet/dotnet run --no-launch-profile`; the API exits and names `Authentication:Authority`. With `Authentication__Authority=http://kc.example` the message names HTTPS.
- Registration: on a reset dev stack, `bootstrap.sh` shows `registrationAllowed` true only after the account import step.
- Existing dev volumes keep registration open until reset or the kcadm command in `app/stack/README.md`.

### Constraints for the Keycloak deployment task
- `KC_PROXY_HEADERS=xforwarded` and an https `KC_HOSTNAME`; SMTP from a SOPS secret; identity provider secrets per DRAFT-7; never add `registrationAllowed` to a realm file.
- The explicit client scope list lacks built-in scopes such as `offline_access`; TASK-3.41 adds them.
<!-- SECTION:NOTES:END -->
