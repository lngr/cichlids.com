---
id: TASK-2.29
title: Deliver external webhooks through an isolated forwarder
status: To Do
assignee: []
created_date: '2026-10-12 04:49'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-2
priority: low
ordinal: 66000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R10; architecture: ADR-0024.

Arbitrary external webhook targets are reached only through a separate forwarder service. The core delivers to the forwarder; a network policy allows the forwarder outbound internet traffic and denies it any traffic into the internal network. Needed once the first external subscriber exists.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an external subscriber registered with the forwarder, When an event is published, Then the subscriber receives it
- [ ] #2 Given a forwarder target URL pointing at an internal service, When an event is delivered, Then the network policy blocks the connection
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
