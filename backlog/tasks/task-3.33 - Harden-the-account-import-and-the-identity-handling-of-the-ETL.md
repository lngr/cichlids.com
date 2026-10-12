---
id: TASK-3.33
title: Harden the account import and the identity handling of the ETL
status: In Progress
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-12 08:12'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
  - app/api/tests/Cichlids.Etl.Tests/KeycloakAdminClientTests.cs
  - app/api/tests/Cichlids.Etl.Tests/KeycloakAdminSettingsTests.cs
parent_task_id: TASK-3
priority: high
ordinal: 55000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R2, R31 (import part), R36.

The profile step replaces only identities of the legacy providers auth0 and legacy-openid. The account import links logins to member profiles only. The Keycloak admin client keeps response bodies out of its errors, has no admin/admin fallback outside Development, and retries 429 and 5xx with a growing delay.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Given an oidc identity on a migrated profile, When the profile step runs again on the dev stack, Then the oidc identity still exists
- [x] #2 Given an archived placeholder profile with a legacy id, When the account import runs, Then no login is linked to it
- [x] #3 Given a Keycloak error during the import, When the operator reads the error output, Then it contains no username and no email address
- [x] #4 Given no admin credentials configured outside Development, When the operator starts the import, Then it aborts with a message naming the missing setting
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
- The profile step deletes and re-inserts only identities of the legacy providers it writes itself: auth0, legacy-openid and email. email counts as legacy because only the ETL writes it from fe_users.email; leaving it out would break a rerun on the unique (provider, subject) index. oidc rows are never touched.
- The account import builds profile links only from member profiles and guards the oidc insert with a join on kind = 'member'. An account whose only profile candidate is not a member is still created in Keycloak but not linked, and the summary counts it.
- The Keycloak admin client reports only operation, HTTP status and a sanitized error code, retries 429 and 5xx with growing delay (honouring Retry-After), and gets its credentials from a resolver that rejects missing credentials and the local admin/admin pair outside Development (the same pattern as SlugSecretResolver; appsettings.json ships admin/admin).

### Steps
1. Replace only legacy identities (R2, AC #1).
   - Failing test first: app/api/tests/Cichlids.Etl.Tests/ProfileMigrationStepTests.cs inserts an oidc identity on legacy profile 10 between the two runs and asserts after the second run that it still exists and that alice has 4 identities; adjust the global identity count at :101. Red today.
   - Production: new app/api/src/Cichlids.Etl/Identity/LegacyIdentityProviders.cs (auth0, legacy-openid, email); Persistence/PgChildRows.cs:322-372 gets an optional scope (column plus allowed values) for the DELETE and a guard that every inserted row lies inside the scope, with an updated class comment; Steps/ProfileMigrationStep.cs:87-95,164-202 uses the scope.
2. Link logins to member profiles only (R31 import part, AC #2).
   - Failing test first: KeycloakAccountImportTests.Scenario seed (:420-449) adds an archived profile kim with a legacy id and an email identity, a fe_users row in Fixtures/keycloak-import-legacy-seed.sql and an Auth0 record in Fixtures/keycloak-import-auth0.ndjson. New test: no oidc identity points at kim's profile. Update the counts in ReportsCreatedUsersOnTheFirstRun (:158-190) and KeycloakAccountImportPagingTests.cs:30-46.
   - Production (Identity/KeycloakAccountImportCommand.cs): LoadProfileIdentitiesAsync (:297-322) joins profile and keeps kind = 'member', counting skipped identities; LoadProfileLegacyIdsAsync (:279-294) keeps members only; InsertOidcIdentitiesAsync (:334-360) inserts only through a join on profile with kind = 'member'; the summary gains ProfileLinksSkippedNotMember and prints it.
3. No PII in errors (R36, AC #3).
   - Failing test first: new KeycloakAdminClientTests with a stub HttpMessageHandler: token 200, then the user list answers 409 with a body containing a username and an email address; the exception message contains 409 and neither the username nor "@".
   - Production (Identity/KeycloakAdminClient.cs): internal constructor accepting an HttpMessageHandler and a delay function (:55-67); EnsureSuccessAsync (:334-344) throws a KeycloakAdminException with operation and status plus the error field only if it matches ^[A-Za-z0-9_.-]{1,64}$, never errorMessage or error_description.
4. Backoff (R36).
   - Failing tests first with recorded delays: 429 then 200 succeeds after 1 s; 503 five times fails after 1, 2, 4, 8 s; Retry-After 3 is honoured (cap 30 s); 400 is not retried; the token endpoint is retried too.
   - Production: SendWithRetryAsync with a request factory (a request message cannot be resent; JSON content is rebuilt per attempt) around SendOnceAsync (:292-304) and the token request (:306-332); the existing 401 refresh (:276-290) stays.
5. No admin/admin outside Development (R36, AC #4).
   - Failing test first: new KeycloakAdminSettingsResolverTests (save and restore environment variables like SlugSecretResolverTests): Staging without credentials throws naming CICHLIDS_KEYCLOAK_ADMIN_USER, CICHLIDS_KEYCLOAK_ADMIN_PASSWORD, Keycloak:AdminUser and Keycloak:AdminPassword; Staging with admin/admin throws; Development without credentials returns admin/admin.
   - Production: new Identity/KeycloakAdminSettingsResolver.cs; Program.cs:110-117 uses it before any connection opens; app/stack/README.md states that the local credentials apply in Development only.

### Verification
- AC #1: automated by step 1. Operator on the dev stack (single steps are available in Development after TASK-3.30): after reset, full run and keycloak-import, note SELECT COUNT(*) FROM profile_identity WHERE provider = 'oidc'; run etl profiles; the count is unchanged.
- AC #2: automated by step 2. Operator: SELECT COUNT(*) FROM profile_identity i JOIN profile p ON p.id = i.profile_id WHERE i.provider = 'oidc' AND p.kind <> 'member'; returns 0.
- AC #3: automated by step 3. Operator: CICHLIDS_KEYCLOAK_ADMIN_PASSWORD=wrong etl keycloak-import prints status and code, no username, no address.
- AC #4: automated by step 5. Operator: DOTNET_ENVIRONMENT=Staging CICHLIDS_SLUG_SECRET=x etl keycloak-import aborts and names the missing setting.
- CI fully green.

### Risks
- A POST retried after a 5xx that Keycloak had committed can answer 409 and abort; rerunning is idempotent. A retried partial import undercounts Created; the read-back still links correctly.
- The archived scenario case changes counts in two test classes.
- Keycloak container tests are slow; the new client tests use a stub only.
- Size M, 5 commits, 1 to 1.5 days.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
##### Summary
- The profile step replaces only identities of auth0, legacy-openid and email (PgChildRows scope); oidc identities survive a rerun.
- The account import links logins to member profiles only (profile join in the identity and legacy-id loads and in the oidc insert); the summary reports ProfileLinksSkippedNotMember.
- KeycloakAdminException carries operation, HTTP status and a short error code, never a response body. 429 and 5xx answers are retried (5 attempts, 1/2/4/8 s, Retry-After honoured, cap 30 s) for admin calls and the token request.
- KeycloakAdminSettings.Resolve accepts the admin/admin fallback in Development only; keycloak-import and reset print the message and exit with 1 otherwise.

##### Tests
- app/api/tests/Cichlids.Etl.Tests/KeycloakAdminClientTests.cs
- app/api/tests/Cichlids.Etl.Tests/KeycloakAdminSettingsTests.cs
- app/api/tests/Cichlids.Etl.Tests/ProfileMigrationStepTests.cs (oidc survives a rerun)
- app/api/tests/Cichlids.Etl.Tests/KeycloakAccountImportTests.cs (archived profile kim, no link)
- app/api/tests/Cichlids.Etl.Tests/EtlProcessTests.cs (Staging without credentials)

##### Operator checks (not automated)
- AC #1: on the dev stack after reset, full run and keycloak-import, note SELECT COUNT(*) FROM profile_identity WHERE provider = 'oidc'; run etl profiles in Development; the count is unchanged.
- AC #2: SELECT COUNT(*) FROM profile_identity i JOIN profile p ON p.id = i.profile_id WHERE i.provider = 'oidc' AND p.kind <> 'member'; returns 0.
- AC #3: CICHLIDS_KEYCLOAK_ADMIN_PASSWORD=wrong etl keycloak-import prints status and error code, no username, no address.
- AC #4: DOTNET_ENVIRONMENT=Staging CICHLIDS_SLUG_SECRET=x etl keycloak-import aborts and names the missing setting.

##### Notes
- ResetCommand takes a settings factory so the environment refusal comes before the credential check.
- A POST retried after a 5xx that Keycloak had committed can answer 409; rerunning is idempotent.
<!-- SECTION:NOTES:END -->
