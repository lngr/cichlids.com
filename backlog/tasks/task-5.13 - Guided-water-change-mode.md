---
id: TASK-5.13
title: Guided water change mode
status: To Do
assignee: []
created_date: '2026-10-16 06:27'
labels: []
dependencies: []
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
