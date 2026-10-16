---
id: TASK-5.13
title: Guided water change mode
status: In Review
assignee: []
created_date: '2026-10-16 06:27'
updated_date: '2026-10-16 07:36'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/water-change.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Care/WaterChangeEvidenceTests.cs
parent_task_id: TASK-5
priority: high
ordinal: 93000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal (decided by the project owner, 2026-10-16)
A wizard guides a water change step by step and produces the evidence for the water change streak as a by-product: checking off heater and filter off, draining the water while a timer runs, a photo with the camera of the app while the tank is partly drained, refilling, heater and filter on again, a second photo. Conditioning and matching the temperature of the fresh water are offered as tips, not as required steps (few members do it). The whole change takes at least 10 to 15 minutes, which makes faking more work than doing it. The timer records how long the water change took; the amount can be compared with the tank volume. The photos are private by default and can be published. A forgotten step (for example the timer) must not block the wizard: it continues with the next step and the evidence notes what is missing.

### Reference
- TASK-5.5 notes (streak rules), TASK-5.11 (guided camera with overlay), TASK-5.1 (care entries).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who starts the water change mode, When they check off the steps, take the drained and the refilled photo with the app camera and finish, Then a water change entry exists with its duration and both photos as private evidence, and it counts as a verified week for the water change streak
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
- Evidence rule (decided by the project owner, 2026-10-16): verified means the guided mode was started and finished in the app with at least 10 minutes between the start of draining and the finish; photos are optional and do not change the evidence state.

- Evidence (decided by the project owner, 2026-10-16): photos are taken with the app camera during the water change; several photos per session are allowed (drained tank, water running in, refilled tank); at least one in-app photo received by the server while the session runs, together with the timer rule, makes it verified. No checks against manipulated clients beyond the server receive time.

- Correction of the note above: the server checks the evidence photos for (1) receipt while the session runs, (2) an EXIF capture time within the session window when present, and (3) single use of the same photo as evidence; the project owner did not ask to drop any of these checks.

### Implementation (2026-10-16)
- Wizard (client-core state machine): heater and filter off, draining with a timer, photos with the app camera (several, up to 6 per session, overlay of the matching photo of the previous water change), optional tips, refilling, heater and filter on, optional amount, summary with duration, photos and evidence state; every step can be skipped without blocking; the wizard resumes after a reload or a closed app.
- Server: water change sessions with server-side step times, a photos table, 24-hour expiry with cleanup, cancel; finishing creates the water change care entry with duration_seconds, evidence and evidence_missing in one transaction with the outbox events. Photos are private like care photos.
- Evidence (pure function WaterChangeEvidences): verified needs the draining step, at least WaterChange:MinDuration (10 minutes) from the start of draining to the refill step or the finish, and at least one qualifying photo; otherwise partial with draining_missing, duration_too_short, photo_missing, photo_outside_session or photo_reused; none for entries logged without the mode. A photo qualifies when the server received it while the session ran, its EXIF capture time (when present) lies within the session plus WaterChange:ClockTolerance (10 minutes; 14 extra hours when the zone is unknown), and its bytes have not served as evidence for an earlier water change of the member. A disqualified photo stays attached as a normal photo.
- Verification on the combined state with TASK-5.16: .NET 529/878 (1 opt-in benchmark skipped)/349 with 0 warnings, client-core and mobile tests and typechecks green, all 33 write e2e specs green (the write stack shortens the minimum duration to 3 seconds).
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Care/WaterChangeEvidenceTests.cs, WaterChangeTests.cs, app/client-core/src/waterChange.test.ts, waterChangeClient.test.ts, app/mobile/src/waterChange/waterChangeText.test.ts, app/mobile/e2e/playwright/water-change.write.mjs.
- Not verified: the wizard on a device with a real camera; the cleanup timer loop (only the expiry pass is tested).
<!-- SECTION:NOTES:END -->
