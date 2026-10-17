---
id: TASK-5.24
title: Today shows followed tanks and milestones
status: In Review
assignee: []
created_date: '2026-10-17 09:32'
updated_date: '2026-10-17 10:36'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: medium
ordinal: 117000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Today gains two sections of the target in DRAFT-11 whose data exists: the updates of the tanks the member follows, and congratulations on milestones.

### Followed tanks
- The followed tanks with their newest update that the viewer may see: a care entry visible to the viewer, a story pointing to the tank, a new time-lapse or tank photo. Ordered by the newest update, at most five, with a link to all followed tanks.
- A tank without an update in the last 30 days is not shown. Visibility follows the rules of TASK-5.7 and TASK-5.15 and the media visibility of ADR-0029.

### Milestones
- First care week: the first week in which a tank met its plan.
- Kept streak: a streak of 4, 12, 26 or 52 weeks.
- Tank anniversary: each full tank year, counted as the freezes count it (decommissioned days shift it).
- A milestone shows on Today from the day it is reached for 7 days, until the member dismisses it; the dismissal is stored per member and milestone. Streak milestones follow the streak switch of TASK-5.19.

### Reference
- DRAFT-11, TASK-5.5, TASK-5.15, TASK-5.19, TASK-6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who follows tanks with fresh visible updates, When Today loads, Then the followed tanks section lists them by their newest update with that update
- [ ] #2 Given an update the member may not see, When Today loads, Then neither the update nor a hint of it appears
- [ ] #3 Given a tank that reached a streak of 4 weeks or a full tank year, When the owner opens Today within 7 days, Then a congratulation shows until the owner dismisses it
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
Sibling endpoints next to the existing Today parts (streaks, heatmap, due care), each section loads on its own.

### API
- GET /api/me/today/followed: followed tanks the viewer may see, newest visible update per tank within 30 days, at most five, hasMore. Visibility from TanksVisibleTo and TanksStreamVisibleTo (care entries without the only-me mark), published stories pointing to the tank, tank and time-lapse photo links through the media link visibility of ADR-0029. Tanks of provisional or suspended owners are left out.
- GET /api/me/followed-tanks: all followed visible tanks (paged) behind the link of the section.
- GET /api/me/today/milestones and PUT /api/me/today/milestones/{key}/dismissal. Milestone key: kind, tank and threshold or year. Table milestone_dismissal (profile, key) via migration.
- Domain: TimeLapseStreaks reach dates per threshold (4, 12, 26, 52 weeks), tank anniversaries over the operating tank year (decommissioned days shift it), first care week from the first kept water change period. Streak milestones follow the streak switch.

### Client and app
- Regenerate the OpenAPI client, client-core mapping, two sections on Today, a followed tanks screen, texts in all 11 catalogs.

### Tests
- API integration tests per AC incl. visibility, switch off and decommissioned days; golden snapshot table rule and migration pin; client-core and mobile unit tests; one Playwright write spec.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Followed tanks
- GET /api/me/today/followed lists the followed tanks (approved follows) with their newest visible update within 30 days, newest first, at most five, with hasMore.
- GET /api/me/followed-tanks pages all followed tanks the member sees, those with the newest update first and the ones without an update last; it is the target of the link on Today (mobile view /followed).
- An update is a story that points to the tank (published, not deleted, author a member who is not suspended), a tank or time-lapse photo (media link visibility of ADR-0029, roles time-lapse, showcase, decoration, technic) or a care entry of the care stream that is not marked only me. Tanks come from TanksVisibleTo and TanksStreamVisibleTo, so a private stream hides care entries while a story stays visible. Tanks of provisional or suspended owners, unpublished and deleted tanks and pending follows are left out. Updates, counts and hasMore come from visible data only.
- The moment of a care entry is its RecordedAt, of a photo the creation of its media link, of a story its publication.
- The details of a page load with one query per kind for all tanks of the page, so the number of queries does not grow with the page size. A tank whose update is gone when its details are read has no update (it drops out of Today and shows without an update in the list).

### Milestones
- GET /api/me/today/milestones (date and utcOffsetMinutes as the streak routes) lists what was reached within the last 7 days and is not dismissed; PUT /api/me/today/milestones/{key}/dismissal stores the dismissal (204, idempotent, 404 for a tank that is not an own live tank). Table milestone_dismissal (profile, key) with a unique index.
- A key is accepted only in its canonical form (Milestones.Key), at most 64 characters, with a streak threshold of 4, 12, 26 or 52 and an anniversary year the tank has completed; every other key answers 400. The canonical key is stored.
- Key: kind, tank and threshold or year (first_care_week:{tank}, kept_streak:{tank}:{4|12|26|52}, tank_anniversary:{tank}:{year}). The key has no run or date, so each achievement of a tank is listed once.
- Kept streak: the time-lapse streak (weeks), reached on the local date of the first photo of the week that completes the length; it follows the streak switch. First care week: the closing day of the first period of the water change streak that a verified water change or a freeze kept; it follows no switch, and the evaluation records no freeze. Tank anniversary: the day the tank year counted in operating days advances (decommissioned days shift it); it follows no switch.
- The routes are open to provisional callers (no follows, own tanks only).

### Verification
- .NET: Api.Tests 2499 passed (1 skipped), Infrastructure.Tests 744, Etl.Tests 351; client-core 351, mobile 543 plus both typechecks; golden snapshot tests green against PostgreSQL 16; full write E2E 55 specs green including today-followed-milestones.write.mjs.
- Tests: Features/Me/TodayFollowedTests, TodayFollowedQueryTests (query count, vanishing update), TodayMilestoneTests, Streaks/MilestoneDomainTests, Provisional/ProvisionalTodaySectionsTests, client-core todaySections.test.ts, mobile todaySectionsText.test.ts, e2e today-followed-milestones.write.mjs.
<!-- SECTION:NOTES:END -->
