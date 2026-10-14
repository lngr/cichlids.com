---
id: TASK-2.31
title: Run the API and the web app as workloads in staging
status: In Progress
assignee: []
created_date: '2026-10-14 09:56'
updated_date: '2026-10-14 09:56'
labels: []
dependencies:
  - TASK-2.9
  - TASK-2.30
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
