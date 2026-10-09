---
id: DRAFT-7
title: Configure Google and Facebook login for Keycloak with the legacy apps
status: Draft
assignee: []
created_date: '2026-09-28 19:22'
labels: []
dependencies: []
parent_task_id: TASK-3
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Migrated members sign in with Google or Facebook and land in their imported account, whose social links the account import stores.

### Context
- The account import links google and facebook identities from the Auth0 export (ADR-0022). Google subjects are global per Google account; Facebook user ids are scoped to the Facebook app that issued them.
- Google Cloud project cichlidscom holds the OAuth client used through Auth0.
- The Facebook app cichlids.com (App-ID 310735852314136) is deactivated after 90 days without API calls and was restricted over missing data deletion handling.

### Scope
- Google: OAuth client for Keycloak (redirect URI of the realm broker endpoint), consent screen verification.
- Facebook: restore access, move to the current Graph API version, data deletion callback or deletion instructions URL, app review for live mode, same app in Keycloak so the stored ids match.
- Keycloak identity providers google and facebook enabled with these credentials via secrets, never in the repository.
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
