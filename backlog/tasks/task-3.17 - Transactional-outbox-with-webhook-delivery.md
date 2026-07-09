---
id: TASK-3.17
title: Transactional outbox with webhook delivery
status: In Review
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-09 03:59'
labels: []
dependencies:
  - TASK-3.1
parent_task_id: TASK-3
priority: medium
ordinal: 37000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Outbox table written in the same transaction as domain changes; dispatcher delivers events to registered webhook endpoints; consumers are idempotent.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a domain change such as a created comment, When the outbox dispatcher runs, Then exactly one event is delivered to a registered webhook endpoint and redelivery is idempotent
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Dispatcher with SKIP LOCKED batching, HMAC-SHA256 signed webhook delivery, exponential backoff with give-up after 10 attempts, admin webhook endpoints. Live smoke: real delivery with independently recomputed signature.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
