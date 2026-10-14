---
id: TASK-2.31
title: Run the API and the web app as workloads in staging
status: In Review
assignee: []
created_date: '2026-10-14 09:56'
updated_date: '2026-10-14 10:15'
labels: []
dependencies:
  - TASK-2.9
  - TASK-2.30
references:
  - apps/api/README.md
  - tools/check-cluster-manifests.sh
parent_task_id: TASK-2
priority: high
ordinal: 69000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
The API, the database migrations and the web app run as containers in the cluster, so staging
serves the MVP over TLS at `staging.cichlids.com` with the API under `/api` on the same origin
(ADR-0027).

### Scope
- Container images: the API (.NET 10, non-root, read-only root filesystem), the ETL/migration
  tool for the schema migration job, and the web app (Expo web export served as static files).
- `apps/api` and `apps/web` with base and overlays staging and prod (prod stays without web login
  until the web scope is decided); a migration Job before the API starts.
- Configuration from secrets created by the operator or through SOPS: database credentials
  (CloudNativePG), the slug secret (TASK-2.23), the Data Protection key ring, the BFF client
  secret, the object storage key of the media bucket; Keycloak authority per environment.
- Traefik routes `staging.cichlids.com/api` to the API and everything else to the web app; the
  placeholder of TASK-2.8 gives way to the web app in staging.
- Image references by digest; the build in CI and the promotion belong to TASK-2.11.

### Out of scope
- Data: staging starts with an empty schema plus synthetic data (TASK-2.17).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the staging overlay synced by Argo CD, When the operator opens https://staging.cichlids.com, Then the web app loads over valid TLS and its API calls to https://staging.cichlids.com/api succeed
- [ ] #2 Given the staging API without the slug secret, When it starts, Then it refuses to start with the slug secret error
- [ ] #3 Given the manifests, When the manifest checks run, Then the API, the migration job and the web app render, pass schema validation and run as non-root with resource limits
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
- Test-first manifest checks for `apps/api` and `apps/web`, Dockerfiles built locally, the API
  image run against a throwaway Postgres, a browser smoke of the web image.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- Images (multi-stage, base images by digest, non-root, read-only root filesystem): API 600 MB,
  migration (ETL `migrate`) 558 MB, web 11.2 MB (Expo web export of the staging variant served by
  BusyBox httpd; the client adds `/api` to `https://staging.cichlids.com`).
- `apps/api` (Deployment, Service, ConfigMap, migration Job as Argo CD PreSync hook) and `apps/web`;
  staging Ingress through Traefik: `/api` to the API unchanged, everything else to the web app,
  cert-manager certificate. Staging uses the web app, prod and previews keep the placeholder; prod
  app workloads stay disabled until the web scope is decided; no API or web preview overlays.
- Secrets (operator): `postgres-app` from CloudNativePG, `api-slugs` (`secret`),
  `api-data-protection` (`key-<uuid>.xml`), `keycloak-web-bff` (`client-secret`), `api-media`
  (`access-key`, `secret-key`). Webhook dispatch disabled, empty host allowlist.
- Resources: API 250m/256Mi (limit 1 CPU/512Mi), web 25m/16Mi, migration 100m/256Mi; node budget
  about 3050m CPU and 6.8 GiB requested with Keycloak, PostgreSQL and Argo CD.
- Image references are digest placeholders that a deployment preflight refuses; TASK-2.11 sets
  the published digests.

### Verified by the agent (Codex) and the main session
- Manifest checks red then green, 285 resources valid; dotnet build and Api tests (421); container
  smoke: migration applied, `/healthz` 200, `/api/pictures` 200; browser smoke: gallery and login
  screen load, `/api/auth/session` 200; without a slug secret the API refuses to start with
  "No slug secret configured for environment Staging"; secret guard and gitleaks clean.
- Known details: a refused start ends with exit code 139 (the runtime cannot write a crash dump on
  the read-only file system); BusyBox answers deep links with `index.html` and status 404.
- Not verified: TLS, Argo CD sync, Keycloak login in the cluster.
<!-- SECTION:NOTES:END -->
