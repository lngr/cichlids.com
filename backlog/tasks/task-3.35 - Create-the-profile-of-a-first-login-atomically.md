---
id: TASK-3.35
title: Create the profile of a first login atomically
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-12 06:40'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: high
ordinal: 58000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R12, R31 (API part).

Profile and oidc identity of a first login are stored in one transaction; on a conflict with a parallel first call the API resolves the existing profile. A login bound to a non-member profile is refused with a clear response and logged.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a new account, When the app sends several authenticated requests at the same moment right after the first login, Then all succeed and exactly one profile exists
- [ ] #2 Given a login bound to an archived profile, When the app calls the me endpoint, Then the API answers with a clear refusal and no server error
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

- **One save for the first login.** `CurrentProfileService` adds the profile together with its oidc identity (through `Profile.Identities`) and saves once. EF Core runs one save as one transaction, so a profile can never exist without its identity. If a caller already holds a transaction, EF Core uses a savepoint, so a failed insert does not break the caller's transaction.
  - The unique indexes already exist: `ix_profile_username` (`ProfileConfiguration.cs:20`, `20260708192431_InitialSchema.cs:871-875`) and `ix_profile_identity_provider_subject` (`ProfileIdentityConfiguration.cs:55`, `InitialSchema.cs:884-888`). No migration is needed.
- **On a conflict, look again, then retry.** If the save fails with a unique violation (Postgres code 23505) on either index:
  - Detach only the two new entries. The scoped DbContext is shared with the rest of the request, so the whole change tracker is not cleared.
  - Look up the profile by subject again. If it exists, a parallel first call won and that profile is returned.
  - Otherwise another account took the username in the meantime. Retry with a fresh candidate, at most 3 attempts.
  - This one loop covers both races, so the code does not need to know which index fired. Rejected: a Postgres advisory lock (does not cover username races between different subjects) and raw INSERT ON CONFLICT (two tables, bypasses EF).
- **ResolveAsync never returns a non-member.** If the subject leads to a profile whose kind is not member:
  - The service logs a warning with profile id, kind and subject (no username, no email) and throws `NonMemberLoginException`.
  - A dedicated `IExceptionHandler` maps that exception to 403 with problem details (`application/problem+json`, a plain-language `detail`, and the stable extension field `code` = `non_member_profile`).
  - An exception plus a central handler was chosen over a return value because all 8 call sites are covered by construction, and a future endpoint cannot forget the check. Today `/api/me` returns 500 (`MeEndpoints.cs:28-29`) and the write endpoints act as the archived profile.
  - 403 was chosen over 401 because the caller is authenticated but not allowed. A 401 would push clients into refresh or sign-out logic.
- **Scope.** API only. The app shows no specific message in this task; TASK-3.40 translates the code `non_member_profile` in the app.
- **Tests.** A deterministic service-level integration test against Testcontainers drives the red phase: an EF save interceptor commits a parallel winner between the lookup and the insert. An HTTP test with concurrent requests is the AC #1 path test. The new test file carries the back-tag `// Story: task-3.35` on line 1, as in `UploadPublishFlowTests.cs:1`.

### Steps

1. **Test support (refactor, no behaviour change)**
   - Add a `CreateDbContext` overload that accepts EF interceptors in `app/api/tests/Cichlids.Api.Tests/ApiFixture.cs` (lines 108-115).
   - Add a small recording logger in `tests/Cichlids.Api.Tests/Features/Auth/RecordingLogger.cs`.
2. **Red: race tests for the first login**
   - New file `tests/Cichlids.Api.Tests/Features/Auth/FirstLoginTests.cs` (back-tag on line 1). It builds `CurrentProfileService` from a fixture context and the app's `Lazy<GeneratedNames>`.
   - (a) Same subject: the interceptor commits the winner (profile "race-user" plus its oidc identity) just before this call saves. Expect the winner's id, exactly one profile and one identity, and no "race-user-2".
   - (b) No orphan: the winner has the same subject but a different username. Expect the winner, and no profile with this call's username remains.
   - (c) Different subject with the same preferred username: this call gets "name-2", and both profiles have an identity.
   - (d) AC #1 over HTTP: one new token, 10 requests started together, mixing `GET /api/me` and `GET /api/me/drafts`. All return 200 with the same profile id, exactly one oidc identity exists for the subject, and only one profile carries the preferred username or a suffix of it.
3. **Green: atomic creation with retry**
   - `app/api/src/Cichlids.Api/Features/Auth/CurrentProfileService.cs`: rewrite `ResolveAsync` (lines 23-34) as a loop of at most 3 attempts (look up by subject, otherwise try to create).
   - In `CreateProfileAsync` (lines 36-63, today two saves at lines 51 and 60), add the identity through the navigation and save once.
   - Catch `DbUpdateException` whose inner exception is a `PostgresException` with code `PostgresErrorCodes.UniqueViolation`, detach both entries, and continue the loop. After the last attempt, throw `InvalidOperationException` with a clear message.
   - Rewrite the class doc comment (lines 10-18) in plain prose without inline tags.
   - Callers stay unchanged; they resolve before opening their own transactions (`PostPublishService.cs:31`, `UploadsWriteService.cs:70,125`, `CommentsWriteService.cs:28`).
4. **Red: refusing a non-member login**
   - In `FirstLoginTests.cs`, add a theory over kinds Archived and System: seed a profile of that kind with a random legacy id plus an oidc identity on it.
     - `GET /api/me` must return 403 with media type `application/problem+json` and `code` = `non_member_profile`.
     - `GET /api/me/drafts` must also return 403, which proves the refusal is central.
     - No new profile may be created for that subject.
   - Service-level test: `ResolveAsync` throws `NonMemberLoginException`, and the recording logger holds one warning naming the profile id and kind.
5. **Green: exception, handler and wiring**
   - New `Features/Auth/NonMemberLoginException.cs` carrying the profile id and kind.
   - New `Features/Auth/NonMemberLoginExceptionHandler.cs`: handles only this exception type and returns false otherwise. It writes the problem details through `IProblemDetailsService`: status 403, title "Login not usable", a `detail` sentence, and the `code` extension.
   - `CurrentProfileService` gets `ILogger<CurrentProfileService>` and checks the kind on the found path.
   - `Program.cs`: add `AddProblemDetails()` and `AddExceptionHandler<NonMemberLoginExceptionHandler>()` near line 78, and `app.UseExceptionHandler()` as the first middleware after `Build()` (before authentication at lines 106-107).
6. **API contract and generated client**
   - Red: an assertion in `OpenApiDocumentTests.cs` that GET `/api/me` lists a 403 response.
   - Green: add `.ProducesProblem(StatusCodes.Status403Forbidden)` to every authenticated endpoint that resolves the profile:
     - `MeEndpoints.cs:12`
     - `UploadsEndpoints.cs:32,41,46,51`
     - `PicturesEndpoints.cs:21`
     - the tank comment POST
     - `CommentsEndpoints.cs:12`
   - Regenerate the client with `pnpm --filter @cichlids/client-core generate` against the running API, and commit `app/client-core/openapi.json` and `src/generated/`.
7. **Backlog**
   - Set the task references to `app/api/tests/Cichlids.Api.Tests/Features/Auth/FirstLoginTests.cs` so the story-test binding gate checks a real test.

### Verification

- **AC #1 (automated):** step 2 tests (a) to (d) are green.
- **AC #1 (operator, dev stack):**
  - A fresh Keycloak user signs in with the app. The Me tab calls `/api/me` and `/api/me/drafts` together, and the profile appears.
  - For another new user, run `for i in $(seq 10); do curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer $TOKEN" http://localhost:5045/api/me & done; wait`. Every line reads 200.
  - SQL: `SELECT count(*) FROM profile_identity WHERE provider='oidc' AND subject='<sub>'` returns 1.
  - SQL: `SELECT count(*) FROM profile p WHERE p.kind='member' AND p.legacy_id IS NULL AND NOT EXISTS (SELECT 1 FROM profile_identity i WHERE i.profile_id=p.id)` returns 0.
- **AC #2 (automated):** step 4 tests are green.
- **AC #2 (operator, dev only):**
  - Run `UPDATE profile_identity SET profile_id=<archived id> WHERE provider='oidc' AND subject='<sub>'`.
  - `curl -i` on `/api/me` shows 403, `Content-Type: application/problem+json` and `"code":"non_member_profile"`. The API log shows one warning with the profile id and kind.
  - Revert the update.
- **Data check before staging:** `SELECT count(*) FROM profile_identity i JOIN profile p ON p.id=i.profile_id WHERE i.provider='oidc' AND p.kind<>'member'` returns 0.
- **DoD:** `dotnet test app/api` is fully green, and client-core typecheck and unit tests are green.

### Risks

- **Global change to error output.** `UseExceptionHandler` with problem details turns every unhandled exception into a 500 problem-details response, also in Development (no HTML developer exception page for API calls). Stack traces are still logged. Accepted as the better API default.
- **The HTTP concurrency test may pass before the fix**, because the in-process race does not trigger every time. The deterministic interceptor tests are the red driver.
- **Detaching instead of clearing.** Only the two new entries are detached, because other services share the scoped DbContext.
- **Mobile until TASK-3.40 lands.** The client builds its error message from the HTTP status text for JSON bodies (`app/client-core/src/client.ts:142-146`). On React Native that text may be empty, and the Me screen then keeps loading (`app/mobile/app/(tabs)/me/index.tsx:63-79`). The case cannot occur once TASK-3.33 is in, and TASK-3.40 translates the code.
- **TASK-3.41 must treat this 403 as "keep tokens, show message".** It is not a sign-out reason and not a reason to refresh the token. A suspension refusal in 3.41 should reuse this path with its own code (for example `account_suspended`), so the client can tell the two apart.
- **Shared files with other tasks.**
  - TASK-3.39 reserves placeholder usernames in the same candidate logic (`CurrentProfileService.cs:70-98`). Land 3.35 first.
  - TASK-3.42 (R32) touches `Program.cs`. Whichever task lands first adds `AddProblemDetails` and `UseExceptionHandler`, and the other reuses them.
  - TASK-3.40 adopts the problem-details `code` field as its error-code convention.
- **Retries run out** only if three conflicting commits land within milliseconds. The result is a logged 500.
<!-- SECTION:PLAN:END -->
