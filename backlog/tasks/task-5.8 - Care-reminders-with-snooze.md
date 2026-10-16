---
id: TASK-5.8
title: Care reminders with snooze
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 04:53'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/care-reminders.write.mjs
  - app/client-core/src/careReminders.test.ts
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

- Story cut: TASK-5.8 covers the reminders of the mobile app (local notifications) and the per-tank reminder switch shared by all devices; Web Push for the web app is TASK-5.12.

### Implementation (2026-10-16)
- Server: tank.care_reminders_enabled (default true); PATCH /api/tanks/{id}/care/plan with remindersEnabled, owner only, outbox tank.care_reminders_changed when the value changes; the plan and GET /api/me/care/due return remindersEnabled.
- Scheduling (pure, app/client-core/src/careReminders.ts): one reminder at 09:00 local time on the due day per enabled rule; overdue care is not repeated, never-logged rules and switched-off tanks get none, past times are skipped, at most 60 pending reminders (iOS keeps 64); Snooze moves one reminder to 09:00 the next day; reconciliation cancels stale reminders.
- Mobile (expo-notifications, app/mobile/src/reminders/): rescheduling at start, on returning to the foreground and after care, plan or switch changes; signing out clears the reminders; tapping opens the care view of the tank; an explanation card before the system permission dialog and a calm hint with a link to the settings when denied. The web build has no local scheduling and shows the switch with a hint to the phone app (Web Push is TASK-5.12).
- Verification: .NET 352/690 (1 opt-in benchmark skipped)/349 with 0 warnings, client-core 104, mobile 290, both typechecks, all 27 write e2e specs green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Care/CareRemindersTests.cs, app/client-core/src/careReminders.test.ts, app/mobile/src/reminders/*.test.ts, app/mobile/e2e/playwright/care-reminders.write.mjs (the switch persists across a reload).
- Verified only by unit tests with a mocked platform: scheduling time, no repetition, snooze, cancelling, tap to open. Not verified: delivery on a device, the system permission dialog, the settings link, the Android channel and iOS category, the Snooze action when the app process was killed.
<!-- SECTION:NOTES:END -->
