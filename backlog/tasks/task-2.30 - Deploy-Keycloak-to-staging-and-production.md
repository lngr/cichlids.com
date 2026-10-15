---
id: TASK-2.30
title: Deploy Keycloak to staging and production
status: In Review
assignee: []
created_date: '2026-10-12 06:41'
updated_date: '2026-10-15 06:30'
labels: []
dependencies:
  - TASK-2.4
  - TASK-2.27
references:
  - apps/keycloak/overlays/staging/cichlids-realm.json
  - apps/keycloak/overlays/prod/cichlids-realm.json
  - docs/adr/0010-gitops-and-infrastructure-as-code.md
  - apps/keycloak/README.md
  - tools/check-cluster-manifests.sh
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
  - `KC_PROXY_HEADERS=xforwarded` behind Traefik;
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

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach (test-first)
- Red first: manifest checks for `apps/keycloak` (base and overlays staging, prod; no preview
  Keycloak) and an extension of `tools/check-keycloak-realm.py` or the manifest test for the
  deployment rules below.

### Deployment
- Keycloak 26.6 (pinned image by digest), one replica per environment, production mode
  (`start --optimized` or `start`), `KC_HOSTNAME=https://auth.staging.cichlids.com` and
  `https://auth.cichlids.com`, `KC_PROXY_HEADERS=xforwarded`, `KC_HTTP_ENABLED=true` only behind
  Traefik inside the cluster; Ingress through Traefik with HTTPS redirect and a cert-manager
  certificate; the admin console and the master realm are not routed publicly (Ingress only for
  `/realms/`, `/resources/` and the paths the login needs; admin through port-forward).
- Database: its own CloudNativePG `Cluster` per environment (stateful rules of TASK-2.7; production
  with the Barman Cloud backup of TASK-2.9 or a database inside the production cluster of
  TASK-2.9, whichever is simpler and keeps one backup path; document it).
- Realm apply: a mechanism that applies `apps/keycloak/overlays/<env>/cichlids-realm.json`
  without ever setting `registrationAllowed` and without touching users, chosen and documented in
  `apps/keycloak/README.md` within ADR-0025 (for example keycloak-config-cli as an Argo CD sync
  hook, or `--import-realm` on first start plus a documented update path).
- Secrets only through SOPS and KSOPS or created by the operator: master admin, database
  credentials (from CloudNativePG), SMTP (host, port, user, password, from), Google and Facebook
  client credentials (DRAFT-7, optional until decided), the BFF client secret (TASK-3.41).

### Operator checks
- Create the secrets, sync, open the discovery document over TLS (AC #1), plain HTTP redirects
  (AC #2), password reset email through the SMTP settings (AC #3, needs a mail provider).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- `apps/keycloak` with overlays staging and prod (no preview Keycloak): Keycloak 26.6.4 pinned by
  digest, one replica, 250m CPU and 768Mi requested; Ingress through Traefik with HTTPS redirect
  and a cert-manager certificate for `auth.staging.cichlids.com` and `auth.cichlids.com`; only the
  cichlids realm, resources and the paths of login and account flows are routed; the admin console
  and master realm stay private (port-forward).
- Realm apply: keycloak-config-cli 6.5.1-26 as an Argo CD PostSync job with `no-delete`; a
  renderer adds mounted secrets and fails on unresolved placeholders; users and the registration
  setting are runtime state and survive re-applies. Limits: not atomic, selective syncs skip the
  hook, removed configuration needs a deliberate operator deletion.
- Database: production uses its own database in the production PostgreSQL cluster (one backup
  path, TASK-2.9); staging without backup bucket access.
- DNS: A records for both auth hosts in the platform tier.
- Secrets created by the operator per environment: `keycloak-admin`, `keycloak-db`
  (basic-auth), `keycloak-smtp`, optional `keycloak-providers`, optional `keycloak-web-bff`.

### Verified by the agent (Codex)
- Manifest checks red then green, 268 resources valid, realm check 69 cases, secret guard,
  platform validate and guardrails, policies 73 tests; local Docker run of Keycloak 26.6.4 with the
  apply: registration false on create and kept after re-apply, users and the BFF secret kept;
  discovery issuer, login resources and a private admin frontend.
- Not verified: TLS issuance, ingress behaviour, SMTP delivery, CloudNativePG reconciliation.

### Open
- Mail provider for SMTP (AC #3).
- Proposed ADR-0025 wording for the apply mechanism (project owner).

### Mail provider (decided by the project owner)
- Mailgun, plan Flex (1,000 messages per month free, then about 0.002 USD per message), sending
  domain in the EU region; Keycloak reaches it through the generic SMTP secret `keycloak-smtp`, so
  the provider stays replaceable. The DNS records of the sending domain (SPF, DKIM, MX, DMARC) go
  into the platform tier once Mailgun shows them.
<!-- SECTION:NOTES:END -->
