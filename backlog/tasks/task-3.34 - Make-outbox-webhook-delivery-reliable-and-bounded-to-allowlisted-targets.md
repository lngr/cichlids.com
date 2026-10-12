---
id: TASK-3.34
title: Make outbox webhook delivery reliable and bounded to allowlisted targets
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: high
ordinal: 57000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R4, R5, R10, R14; architecture: ADR-0024.

Webhook targets must be http or https on a host from the per-environment allowlist; redirects are not followed. Each event stores its next attempt time; the delay doubles from the previous attempt up to one hour, and an event is given up only after seven days. An administrator can redeliver given-up events. The dispatcher selects only due events through a partial index, reserves them in a short transaction, delivers outside the transaction with a ten-second timeout and records the result. A failure of one event never stops other events or the API host.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a webhook registration for a host outside the allowlist or with an ftp URL, When the administrator submits it, Then the API rejects it
- [ ] #2 Given a subscriber that is down for two hours, When it comes back, Then it receives the events published in the meantime
- [ ] #3 Given a corrupt event payload in the outbox, When the dispatcher runs, Then the API keeps serving requests and other events are delivered
- [ ] #4 Given a given-up event, When the administrator triggers redelivery, Then the subscriber receives it
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
