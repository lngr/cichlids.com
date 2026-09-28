---
id: TASK-3.15
title: Auth0 account import into Keycloak
status: In Progress
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-09-28 14:03'
labels: []
dependencies:
  - TASK-3.13
  - TASK-3.3
parent_task_id: TASK-3
priority: medium
ordinal: 35000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Import legacy accounts into Keycloak so returning members keep their identity, sized for the win-back campaign.

### Scope
- Import every legacy account with a known email address (Auth0 export joined with fe_users emails, deduplicated), not only accounts with a migrated profile.
- Accounts with a migrated profile are linked to it (oidc identity); accounts without a profile get one on first login through the existing /api/me flow.
- Password users get the required action update password (the export has no hashes); all imported accounts get the required action terms and conditions, so the first login runs through the campaign gate.
- Accounts without any known contact data and without a profile are not imported.
- Social identities (Google, Facebook) are prepared as identity provider links.
- The import is repeatable (idempotent on email).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the Auth0 export and migrated profiles, When the import runs, Then every active account exists in Keycloak linked to its profile, duplicate emails are consolidated per the agreed merge rules, and password users require a reset on first login
- [ ] #2 Given the Auth0 export and the legacy user table, When the import runs twice, Then every account with a known email exists exactly once in Keycloak with the agreed required actions, profile-linked where a profile exists, and accounts without contact data and without profile are absent
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
New ETL command `keycloak-import` in Cichlids.Etl. It builds one account per known email from the Auth0 export (NDJSON, /workspaces/legacy-data/auth0/auth0-cichlids.json) joined with fe_users and fe_users_auth0, creates missing Keycloak users through the Admin REST API, and links every account that has a migrated profile by an oidc profile_identity row. The import is idempotent on email.

### Data facts (real legacy data)
- Auth0 export: 12,147 records (11,575 password, 320 google-oauth2, 252 facebook); 12,044 distinct lowercased emails; 26 facebook records without email; 75 emails occur more than once.
- fe_users: 11,948 rows, 11,926 distinct emails, all of them contained in the Auth0 export.
- fe_users_auth0 maps 816 Auth0 subjects to fe_users uids; 21 of the 26 email-less facebook records map to fe_users without email, 6 of those have a migrated profile.
- Expected result: 12,044 email accounts plus 6 email-less facebook accounts with profile = 12,050 Keycloak users (plus the dev users).

### Merge rules (pure, unit tested: LegacyAccountPlanner)
- Key: lowercased, trimmed email. Email source per Auth0 record: its own Email, else the fe_users email via fe_users_auth0.
- One account per key. Auth0 records sharing the key merge: password connection sets UPDATE_PASSWORD; google-oauth2|X and facebook|X become federated identity links (google/X, facebook/X); emailVerified is true when any merged record is verified.
- Email-less records resolve to a profile via the auth0 profile_identity (sub). With a profile they become an account keyed by the Auth0 id (username facebook-<id>, no email). Without profile they are dropped and counted.
- Every account gets TERMS_AND_CONDITIONS; password accounts additionally UPDATE_PASSWORD.
- Username: the email (loginWithEmail stays on); Keycloak user id: deterministic UUIDv5 over the key, so re-runs and the profile link agree without lookups.
- Profile link: profile found via email identity, else via auth0 identity of any merged record; an oidc profile_identity (provider oidc, subject = Keycloak user id) is upserted. CurrentProfileService resolves oidc first, so the link is effective on first login.

### Keycloak side (KeycloakAdminClient, HttpClient, no new package)
- Admin token via master realm password grant (admin-cli), configurable (Keycloak:Url, Keycloak:Realm, Keycloak:AdminUser, Keycloak:AdminPassword; env overrides).
- Realm prerequisites ensured idempotently: required action TERMS_AND_CONDITIONS enabled; identity providers google and facebook exist (disabled, placeholder client ids, so links can be stored before the providers are configured).
- Existing users are paged once (brief representation) into email -> id; an existing email is never recreated or altered (a self registered account keeps its state), its id is used for the profile link.
- Missing users are created through partialImport in chunks (ifResourceExists SKIP) with id, username, email, emailVerified, enabled, requiredActions, federatedIdentities.
- Realm file app/stack/keycloak/cichlids-realm.json gains the same required action and providers for fresh stacks.

### Red-Green order
1. Unit tests LegacyAccountPlannerTests (Etl.Tests): merge by email, email via fe_users_auth0, email-less with/without profile, required actions per connection, federated links, deterministic id. Red first, then planner.
2. Integration test KeycloakAccountImportTests with Testcontainers: MySQL fixture (fe_users, fe_users_auth0 rows), Postgres with migrated profiles and identities, Keycloak 26.6.4 container with the realm file, Auth0 NDJSON fixture file. Run the import twice, then assert: each known email exists exactly once, required actions as agreed, federated links present, profile-linked users have an oidc identity with their Keycloak id, email-less without profile absent; a password grant for an imported password user fails with account not fully set up. This is the story's end-to-end path (import command against real Keycloak and Postgres) and is referenced via --ref.
3. Wire into Program.cs and app/stack/bootstrap.sh as an idempotent step (Auth0 export path configurable, step skipped with a message when the file is absent); README documents it.

### Affected files
- app/api/src/Cichlids.Etl/Identity/ (Auth0ExportReader, LegacyAccountPlanner, KeycloakAdminClient, KeycloakAccountImportCommand)
- app/api/src/Cichlids.Etl/Program.cs, appsettings.json
- app/api/tests/Cichlids.Etl.Tests (planner tests, import tests, fixtures), csproj gains Testcontainers.Keycloak
- app/stack/keycloak/cichlids-realm.json, app/stack/bootstrap.sh, app/stack/README.md
- docs/adr/0022 account import into Keycloak (merge key, required actions, deterministic ids)

### Risks and assumptions
- The Auth0 export has no password hashes; imported password users can only get in through a reset or an execute-actions email (campaign, DRAFT-1). The local stack has no SMTP; the observable proof is the pending required actions.
- Social logins work only once google/facebook client credentials exist; links are stored ahead of that.
- Importing all emails blocks self registration with a legacy email (duplicate emails are rejected), which protects the email fallback in CurrentProfileService from takeover by an unverified registration.
- AC wording mentions active accounts: all fe_users rows are deleted=0 and disable=0, so every account with a known email counts as active.
<!-- SECTION:PLAN:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
