---
id: TASK-5.12
title: Web push reminders with consent
status: In Progress
assignee: []
created_date: '2026-10-16 04:37'
updated_date: '2026-10-16 04:53'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 92000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Members of the web app can opt in to care reminders through Web Push with our own VAPID key pair (RFC 8292) and encrypted payloads (RFC 8291). The decisions of the project owner are in TASK-5.8 (delivery, consent): off by default, an explanation before the browser asks for permission, switching off deletes the subscription. The per-tank reminder switch and the reminder rules of TASK-5.8 apply.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a web app member who agreed to the explanation and allowed notifications, When a water change of their tank becomes due, Then the browser shows one notification on the due day that can be snoozed, and switching reminders off removes the subscription
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
