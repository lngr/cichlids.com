---
id: TASK-4.2
title: App texts in the launch languages with device language and fallback
status: To Do
assignee: []
created_date: '2026-10-15 08:46'
labels: []
dependencies: []
parent_task_id: TASK-4
priority: high
ordinal: 73000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Every app text comes from translation files from the first screen on; English is the default and
fallback; the app follows the device language and the member can change it; numbers, dates and
plurals follow the language; the layout works right to left. Launch languages: English, Spanish
(Latin America), Portuguese (Brazil), German, French, Italian, Polish, Dutch, Czech, Indonesian,
Turkish; machine translated.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a device set to Brazilian Portuguese, When the member opens the app, Then all screens show Portuguese texts with Brazilian number and date formats, and after switching to German in the settings they show German
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
