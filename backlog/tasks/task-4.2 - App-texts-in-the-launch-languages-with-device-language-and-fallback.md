---
id: TASK-4.2
title: App texts in the launch languages with device language and fallback
status: In Review
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 09:14'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/localization.write.mjs
  - app/mobile/src/i18n/localization.test.ts
  - app/mobile/src/i18n/preferences.test.ts
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

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- Catalogs for en (default and fallback), es-419, pt-BR, de, fr, it, pl, nl, cs, id, tr (machine
  translated), 209 keys each plus the plural forms each language needs; a test fails on a missing
  key and on literal strings in screens.
- The app follows the device language, the member chooses another in Me (kept per device);
  numbers and dates through Intl with the chosen locale; screen errors translated.
- RTL: layout with start and end; a pseudo-RTL mode is covered by a Playwright check (no clipped
  tab labels).

### Verified by the agent (Codex) and the main session
- Red first (missing catalogs and setting, formats, raw API errors, clipped RTL tab labels);
  mobile 215, client-core 15, .NET 1,101; write suite 14 of 14 including
  `localization.write.mjs` and the feature flag spec on the combined state.
- Not verified: native devices.
<!-- SECTION:NOTES:END -->
