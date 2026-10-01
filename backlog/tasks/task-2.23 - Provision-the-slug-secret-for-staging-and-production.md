---
id: TASK-2.23
title: Provision the slug secret for staging and production
status: To Do
assignee: []
created_date: '2026-10-01 07:08'
labels: []
dependencies: []
parent_task_id: TASK-2
priority: high
ordinal: 27000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Every deployed environment runs the API and the ETL with one stable slug secret, set before the first production import.

### Context
- The slug secret (configuration Slugs:Secret, environment variable CICHLIDS_SLUG_SECRET) keys the hash that derives picture slugs, generated user<number> handles, Keycloak login usernames and guest_<number> names during the legacy import and in the API.
- A different secret produces different names and links; after the production import it must never change.
- Outside Development, API and ETL refuse to start without a secret or with the local dev value (feat/legacy-mvp, app/stack/README.md).

### Scope
- Generate the secret per environment (staging, production), store it encrypted with SOPS and age (TASK-2.6), deliver it to the API workload and to the ETL job as CICHLIDS_SLUG_SECRET.
- The same production secret for the initial legacy import and the running API.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a staging deployment of the API and the ETL import job, When both start, Then they run with the environment's slug secret from SOPS, and a start without it fails with the slug secret error
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
