---
id: TASK-5.12
title: Web push reminders with consent
status: In Review
assignee: []
created_date: '2026-10-16 04:37'
updated_date: '2026-10-16 05:44'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/web-push.write.mjs
  - >-
    app/api/tests/Cichlids.Api.Tests/Features/WebPush/WebPushSubscriptionTests.cs
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation (2026-10-16)
- Server: subscriptions per member and browser (endpoint on an allowlist of push service hosts, keys, IANA time zone, failure count; at most 10 per member; subscribing one endpoint at a time through an advisory lock), VAPID settings from configuration (the API refuses to start outside Development when Web Push is enabled without keys), a background sender that sends one encrypted message per rule and due date at 09:00 in the browser's time zone (Lib.Net.Http.WebPush 3.3.1, RFC 8291 aes128gcm, RFC 8292), deletes subscriptions on 404 or 410 and backs off on other failures; server-side snooze; outbox events for subscribe, unsubscribe and snooze. The payload carries the tank id and title, the care kind, the due date and the URL.
- Web app: reminder setting off by default; the consent explanation comes before the browser permission prompt; a service worker shows, opens and snoozes the reminders and translates them from the catalogs; a web app manifest lets iOS add the app to the home screen; unsupported browsers and iOS Safari outside a home-screen app see a calm hint.
- Consent per account: an existing browser subscription is resynchronised only for the account that subscribed in this browser; a subscription another account left behind is removed from the browser. Switching off and signing out always clean up the browser, also when the server is unreachable (the server deletes a leftover subscription on the next 410).
- Operator: key pair outside the repo with tools/create-vapid-keys.cs, Secret api-web-push (optional reference in the manifests), WebPush__Enabled and WebPush__Subject in the overlay settings; see apps/api/README.md, section Web Push reminders. Both overlays ship with Web Push disabled.
- Verification: see TASK-5.11 (combined state).
- Tests: app/api/tests/Cichlids.Api.Tests/Features/WebPush/*, app/client-core/src/webPushClient.test.ts, app/mobile/src/webPush/*.test.ts, app/mobile/e2e/playwright/web-push.write.mjs.
- Not verified: delivery through a real push service, Safari and Firefox, a real iOS home-screen app, the Snooze click on a real notification, egress from the cluster to the push services.
<!-- SECTION:NOTES:END -->
