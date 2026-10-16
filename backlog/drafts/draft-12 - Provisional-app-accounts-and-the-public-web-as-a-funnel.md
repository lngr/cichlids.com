---
id: DRAFT-12
title: Provisional app accounts and the public web as a funnel
status: Draft
assignee: []
created_date: '2026-10-16 05:30'
updated_date: '2026-10-16 16:32'
labels: []
dependencies: []
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal (concept of the project owner, 2026-10-16; details being worked out)
- Installing the app creates a provisional, anonymous account (for example member 48213) without e-mail or password; registering turns it into a full account and keeps everything. Posting an own public profile requires registration; using the app does not.
- The public web version is a funnel to the app: profiles and tanks are indexable for search engines, visitors see what the app does and how active it is; the care stream of a tank shows only the latest 2 or 3 entries on the web, with an invitation to see more after registering or in the app.

### Proposal of the main session (not decided)
- Provisional accounts may browse everything, follow tanks and keep private tanks with care entries; registering is needed for everything others see (public tank, profile, comments, kudos, questions).
- Open architecture decision: how a device authenticates without a login (device key pair or anonymous token), how data moves on registration, the loss of an unregistered account on uninstall.
- Web: indexable tank profiles, species pages, articles, hall of fame; stream teaser with app store and deep links; visible activity numbers; interactions lead to the app.
- This refines TASK-5.7 (access without an account: full stream in the app, teaser on the web).
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Decided by the project owner (2026-10-16)
- Provisional accounts may create their own tanks and log care, private until the member registers; registering keeps everything.

- Device authentication (decided by the project owner, 2026-10-16): the device creates a key pair and keeps the private key in the secure store (Keychain or Keystore); the API knows the public key and issues short-lived tokens to the provisional profile after a signed request; registering links the Keycloak account to that profile. Needs an ADR before implementation.

- ADR-0030 (Proposed) describes the device accounts for discussion with the project owner before any build.
<!-- SECTION:NOTES:END -->
