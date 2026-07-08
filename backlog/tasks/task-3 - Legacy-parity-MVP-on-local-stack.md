---
id: TASK-3
title: Legacy parity MVP on local stack
status: To Do
assignee: []
created_date: '2026-07-08 18:58'
labels: []
dependencies: []
priority: high
ordinal: 20000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Rebuild the complete legacy feature set on the new stack (ASP.NET Core 10 API, Postgres, Keycloak, RustFS object store, Expo client) with the legacy data migrated, usable end to end on the local compose stack.

### Scope
- New relational domain schema with a legacy_id bridge on every migrated entity
- Idempotent ETL from the legacy MySQL into Postgres
- Media pipeline seeding originals and variants into the object store
- API with full legacy parity (pictures, tanks, comments, ratings, species, users, upload)
- Generated TypeScript client and Expo app core screens
- Local Keycloak auth with Auth0 account import

### Out of scope
- Production deployment (the GitOps app tier is separate work)
- Video transcoding, reputation formulas, care/tracking layer, AI agents
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
