---
id: DRAFT-1
title: Member win-back email campaign
status: Draft
assignee: []
created_date: '2026-07-09 09:46'
updated_date: '2026-10-15 06:32'
labels: []
dependencies:
  - TASK-3.15
parent_task_id: TASK-3
priority: medium
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Bring former members back: an email to every imported account announcing the relaunch, framed as a password change and terms update notice.

### Notes
- Needs an email sending provider decision (deliverability for ~12k mails) and legal review of the notice framing.
- Landing flow: email link, Keycloak required actions (password, terms), then straight into the app.
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
### Mail sending (project owner, 2026-10-15)
- Transactional mail (Keycloak verification and password reset) and campaign mail use separate
  sender domains (for example `mg.cichlids.com` and `news.cichlids.com`) and separate credentials,
  so campaign bounces and complaints never harm the delivery of password resets; the campaign
  provider is a configuration choice (Mailgun Flex, a one-month plan or another bulk provider).
- Cost estimate with Mailgun Flex: about 24 USD for a one-time campaign to about 12,000 members.
- Risk: many legacy addresses no longer exist; high bounce rates can suspend the sending account.
  Validate addresses before sending, send in staggered batches, ideally from a separate account.
<!-- SECTION:NOTES:END -->
