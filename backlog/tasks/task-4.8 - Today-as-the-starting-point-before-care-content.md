---
id: TASK-4.8
title: Today as the starting point before care content
status: In Review
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 12:25'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/today.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Me/TodayTests.cs
parent_task_id: TASK-4
priority: medium
ordinal: 79000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Today is the first tab; before the care features of slice 2 it shows a minimal, useful start.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a signed-in member, When they open the app, Then Today shows its start content and links into their tanks and Discover
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
### Content of Today (decided by the project owner, 2026-10-15)
1. Greeting with the name; returning legacy members see "Welcome back, a member since <year>".
2. Your tanks as cards with the newest photo, linking to the tank page; without a tank "Create
   your first tank".
3. Open drafts if any: "Continue editing".
4. "On this day X years ago" for legacy members: an own photo from this calendar day of an
   earlier year, if one exists.
5. Tank of the week from the hall of fame of the member's interest region, linking to Discover.
Sections 2 to 4 disappear without own content, and the tank of the week moves up. Slice 2 adds
due care tasks and the streak above section 2.

- The target Today of the mockup and the vision is kept in DRAFT-11; each of its parts replaces
  the reduced Today as soon as the slice that delivers its data exists.

### Implementation (2026-10-15)
- GET /api/me/today?date=YYYY-MM-DD (authenticated; the app sends the local calendar date, coded problem today_invalid_date otherwise) returns the display name, the member-since year for legacy profiles (registration date of the archive), up to 10 published tanks with their newest photo, the number and the newest three open drafts, one own photo of an earlier year on the same month and day (29 February only on 29 February) and the tank of the week of the first interest region that has one, otherwise of the first selectable region; it reuses the cached and visibility-filtered Discover result.
- App: Today shows greeting, Your tanks (otherwise Create your first tank opening the onboarding), Continue editing, On this day X years ago and the tank of the week with a link to Discover; sections without content disappear. Anonymous visitors see a short welcome with sign-in and Discover. Loading and error states with retry, 48dp, light and dark, all 11 catalogs.
- Verification on the combined state of TASK-4.7 and TASK-4.8: .NET 325/509 (1 opt-in benchmark skipped)/342 with 0 warnings, client-core 28, mobile 244, both typechecks, all 21 write e2e specs green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Me/TodayTests.cs, app/client-core/src/today.test.ts, app/mobile/src/today/localDate.test.ts, app/mobile/e2e/playwright/today.write.mjs.
- Not verified: native Android and iOS runs, remote CI.
<!-- SECTION:NOTES:END -->
