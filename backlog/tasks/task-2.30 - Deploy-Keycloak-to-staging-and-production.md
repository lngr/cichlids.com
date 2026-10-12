---
id: TASK-2.30
title: Deploy Keycloak to staging and production
status: To Do
assignee: []
created_date: '2026-10-12 06:41'
labels: []
dependencies:
  - TASK-2.4
  - TASK-2.27
references:
  - apps/keycloak/overlays/staging/cichlids-realm.json
  - apps/keycloak/overlays/prod/cichlids-realm.json
  - docs/adr/0010-gitops-and-infrastructure-as-code.md
parent_task_id: TASK-2
priority: high
ordinal: 68000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Keycloak runs in the cluster for staging and production. Each environment uses its realm from `apps/keycloak/overlays/{staging,prod}`, reachable over TLS at `auth.staging.cichlids.com` and `auth.cichlids.com`.

### Scope
- **Realm apply mechanism:** choose `--import-realm` or keycloak-config-cli (ADR-0010).
  - Applying the realm must never set `registrationAllowed`.
  - If a re-apply resets it, re-run `keycloak-import --open-registration` after the apply in production only.
- **Secrets via SOPS and KSOPS (TASK-2.6):**
  - SMTP settings for email verification and password resets (ADR-0022);
  - Google and Facebook client credentials (DRAFT-7);
  - the Keycloak database credentials.
- **Admin bootstrap:**
  - The master admin comes from a secret and is never `admin/admin`.
  - Realm admin and tester accounts are created by the operator outside Git, following `apps/keycloak/README.md`.
- **Proxy and hostname:**
  - `KC_PROXY_HEADERS=xforwarded` behind ingress-nginx;
  - an https `KC_HOSTNAME` per environment;
  - the admin console is not publicly exposed.
- **Database:** Postgres through CloudNativePG (TASK-2.9) with `Prune=false` (ADR-0014).

### References
- ADR-0010, ADR-0014, ADR-0022, ADR-0023
- TASK-2.27 (realm files and constraints), TASK-2.6, TASK-2.8, TASK-2.9, DRAFT-7
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the staging overlay synced by Argo CD, When the operator opens https://auth.staging.cichlids.com/realms/cichlids/.well-known/openid-configuration, Then it is served over valid TLS and the issuer is https://auth.staging.cichlids.com/realms/cichlids
- [ ] #2 Given Keycloak in staging, When a plain HTTP request reaches the cichlids realm through the ingress, Then Keycloak redirects or refuses it and never serves the login page over HTTP
- [ ] #3 Given the deployed Keycloak, When the operator triggers a password reset for a test account, Then the reset email arrives through the SMTP settings from SOPS
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
