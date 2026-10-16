---
id: TASK-5.8
title: Care reminders with snooze
status: To Do
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 04:37'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 88000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
On the due day the member receives one push notification per due care, which they can snooze; reminders can be switched off per tank.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a due water change and reminders switched on, When the due day arrives, Then the member receives one push notification and can snooze it
<!-- AC:END -->

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
### Delivery (decided by the project owner, 2026-10-16)
- The mobile app schedules the care reminders as local notifications on the device (no server push, no third party); it reschedules them whenever the plan or the entries change and when the app opens. Server push is decided later for kudos and answers.
- The way web app users are reminded is still open.

- Web app (decided by the project owner, 2026-10-16): Web Push with our own VAPID key pair (RFC 8292) and encrypted payloads (RFC 8291), only after the member allows notifications in the browser; no additional service provider. The private VAPID key lives only on the server (SOPS secret in the cluster, a local file in development); the operator generates the key pair. On iOS, Web Push works only for a web app added to the home screen.

- Consent (decided by the project owner, 2026-10-16): Web Push stays off by default. Before the browser asks for permission, the web app explains that the push service of the browser maker (Google, Mozilla or Apple) learns that this browser receives notifications from cichlids.com and when, but not their content; the member agrees explicitly. The member can switch it off at any time, which deletes the subscription on the server.
<!-- SECTION:NOTES:END -->
