---
id: DRAFT-10
title: Record every API retrieval as an event and derive views from it
status: Draft
assignee: []
created_date: '2026-09-29 06:25'
labels: []
dependencies: []
parent_task_id: TASK-3
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Every API retrieval of a picture (and other public resources) is recorded as an event, and view counts are derived from that event log by debouncing afterwards.

### Context
- GET /api/pictures/{slug} increments view_count directly on every call, including reloads such as the refresh after posting a comment and calls from external clients or tests.
- A retrieval is not the same as a view; views result from debouncing retrievals (for example one per visitor, resource and time window).

### Scope
- Emit a retrieval event per API call (resource, time, anonymous or account identity, client information within privacy limits) into an event log or stream (Kafka or another form; relation to the transactional outbox to be decided).
- A consumer derives view counts by debouncing; view_count becomes a projection that can be rebuilt from the log.
- The same events feed telemetry and monitoring.
- Privacy review of what identifies a visitor (IP address, cookie, account).
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
