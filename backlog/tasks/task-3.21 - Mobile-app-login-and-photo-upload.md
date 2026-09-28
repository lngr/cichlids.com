---
id: TASK-3.21
title: Mobile app login and photo upload
status: In Progress
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-09-28 14:38'
labels: []
dependencies:
  - TASK-3.19
  - TASK-3.16
parent_task_id: TASK-3
priority: medium
ordinal: 41000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Keycloak login in the app and the photo upload plus publish flow.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the local stack, When a user logs in via Keycloak in the app and uploads a photo, Then the published photo appears in the gallery, verified by a Maestro flow
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
The app gets a Keycloak login (OIDC authorization code with PKCE, public client cichlids-app) and an upload flow on top of the TASK-3.16 endpoints: pick a photo, upload it as a draft, enter title/description/topic, publish (or discard), land on the published picture detail. Anonymous browsing stays unchanged.

### Auth (app/mobile/src/auth)
- expo-auth-session (+ expo-web-browser, expo-crypto) with Keycloak discovery http://localhost:8180/realms/cichlids (config: EXPO_PUBLIC_KEYCLOAK_URL, app.json extra keycloakUrl, default localhost:8180), client cichlids-app, scopes openid profile email, redirect from makeRedirectUri (web: the app origin; native: cichlids://auth).
- AuthProvider context: login(), logout() (end session), accessToken with refresh via refresh_token before expiry; tokens persisted with expo-secure-store on native and sessionStorage on web (storage adapter).
- apiClient receives getAccessToken from the provider (client-core already supports it).
- Profile tab entry: a Me screen (app/(tabs)/me) showing the logged-in profile from GET /api/me with login/logout buttons; the tab shows Login when anonymous.
- Realm file: cichlids-app redirectUris gains cichlids://* for native builds.

### Upload flow (app/mobile/app/upload)
- Entry: an Upload button in the gallery header, visible when logged in (else it routes to login).
- expo-image-picker (library; camera later), JPEG output with quality 0.9 so HEIC never reaches the API.
- Uses client-core helpers from TASK-3.16 (uploads.create, posts.publish, posts.discard; multipart via FormData, web File/Blob and native uri objects).
- Screen states: picking, uploading (progress indicator), form (title required, description, topic chips cichlids/tanks/offtopic), publishing, error with retry; discard deletes the draft and returns.
- After publish: router.replace to /gallery/{slug}; the gallery list refetches on focus so the new picture shows first.
- i18n keys in en.json and de.json; theme tokens only; touch targets >= 48dp; testIDs for E2E (login-button, upload-button, upload-pick, upload-title, upload-publish, picture-title).

### Isolated write E2E stack (no writes to the seeded dev DB)
- Script app/mobile/e2e/write-stack.sh: creates a fresh database cichlids_e2e on the stack Postgres (drop/create), runs etl migrate against it, uses bucket cichlids-e2e on RustFS, starts a second API on :5046 (env overrides for connection string, bucket, public base URL) and Expo web on :8082 with EXPO_PUBLIC_API_URL=http://localhost:5046; waits for health; tears down on exit. Keycloak is shared (login of dev-user only).
- Playwright spec app/mobile/e2e/playwright/upload.mjs: login through the real Keycloak page as dev-user, upload a generated JPEG via the file input, fill title, publish, assert the detail shows the title and the gallery lists it first. Tagged TASK-3.21, referenced via --ref.
- Maestro flow app/mobile/e2e/maestro/login-upload.yaml with tag TASK-3.21 for the native path (Maestro runner is blocked on device attach; see PROJECT-STATUS F13).

### Red-Green order
1. Unit tests (jest-expo or vitest where the mobile project has its test setup; add jest-expo if none) for the token storage adapter and the auth refresh decision logic, red first.
2. Playwright upload spec red against the write stack (no login button), then implement auth, then the upload flow until green.
3. Existing web smoke stays green.

### Affected files
- app/mobile/package.json (expo-auth-session, expo-web-browser, expo-crypto, expo-secure-store, expo-image-picker via expo install), app.json
- app/mobile/src/auth/*, src/api/client.ts, app/_layout.tsx, app/(tabs)/_layout.tsx, app/(tabs)/me/*, app/upload/*, app/(tabs)/gallery/index.tsx, src/i18n/*.json
- app/mobile/e2e/write-stack.sh, e2e/playwright/upload.mjs, e2e/maestro/login-upload.yaml, package.json scripts
- app/stack/keycloak/cichlids-realm.json, app/stack/README.md

### Risks and assumptions
- The AC names a Maestro flow; Maestro cannot drive a device here, so the green E2E evidence is the Playwright web flow against the isolated write stack, and the Maestro flow is written but unexecuted.
- Keycloak login on Android needs the cichlids:// redirect in the realm; the existing dev realm needs that change applied (bootstrap skips an existing realm), handled by the write-stack script or documented.
- Refresh token rotation on the web relies on sessionStorage; a full page reload keeps the session within the tab.
<!-- SECTION:PLAN:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
