---
id: TASK-5.5
title: Weekly streak with vacation freeze and heatmap
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 12:35'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/streaks.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Streaks/WaterChangeStreakTests.cs
parent_task_id: TASK-5
priority: medium
ordinal: 85000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A streak counts weeks with completed care, survives vacations through a freeze, shows a heatmap over 16 weeks and can be switched off in the settings.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member who logged care in each of the last three weeks, When they open their streak, Then it shows three weeks and the heatmap marks those days
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
### Streak rule (decided by the project owner, 2026-10-15)
- A week (ISO week) counts when the member has at least one care entry in it (water change or measurement, any tank).
- Up to 2 missed weeks are bridged automatically by a freeze; every 4 kept weeks give back one freeze, at most 2 in reserve.
- The streak can be switched off in the settings.

### Open: freeze and visibility (raised by the project owner, 2026-10-15)
- Care entries are self-reported, so a member could claim a water change. The freeze rule above is on hold until the project owner decides between: a private streak with freeze (no kudos on streaks), a public streak with freeze, a streak without freeze, or no streak (heatmap and plan only).
- Do not implement this story before that decision; TASK-5.4 shows due care first and adds the streak afterwards.

- Scope includes the streak on Today: TASK-5.4 leaves an empty StreakSlot in app/mobile/app/(tabs)/today.tsx for it.

### Rethink (project owner, 2026-10-16)
- A streak built on self-reported care entries can be faked, unlike Strava (recorded activity) or Duolingo (completed lessons); a private streak with freeze is rejected. The streak should depend on verifiable factors, for example a current photo taken in the app each time, which also feeds the timeline; but any other subject could be photographed and would pollute the timeline. The concept is being reworked with the project owner; do not implement before.

### Decided by the project owner (2026-10-16)
- Several streaks can exist side by side.
- A time-lapse streak: one guided in-app photo per week (TASK-5.11 camera) keeps it; it brings members back into the app. It earns reputation, has a leaderboard (how many members hold the streak, how long) from which the time-lapses can be watched directly.
- Care heatmap (water changes, measurements) stays independent of the streak.
- No freeze: instead the member declares a vacation, which pauses the streak for a good reason (nobody can change water while away); frequent vacations become visible.
- The care (water change) streak is still being designed: self-reported entries are too easy to fake or to cheat oneself with; the project owner asks for creative ways to make it verifiable.

- Time-lapse streak, anti-fake (decided by the project owner, 2026-10-16): a photo taken with the camera of the app (not picked from the library) counts as sufficient proof; an image model that checks the subject comes later, when fakes become a problem and enough data exists to train it. Members want clean time-lapses, so a fake photo shows in their own time-lapse. Deleting a photo that keeps the streak asks first, because it breaks the streak.
- Leaderboard and ranking are a later feature (DRAFT-14), not part of this story; the onboarding stays free of points and rankings.
- Water change streak: test kits, test strips and sensors are owned by few members (often teenagers without any); evidence must work without extra hardware. A water change photo pair (the tank half drained, then refilled) is worth pursuing; open: how to tell that it was drained, and what happens when the member forgets the photo during the change.

### Decided by the project owner (2026-10-16), continued
- Water change streak: the evidence comes from the guided water change mode (TASK-5.13): checked steps, a timer of at least 10 to 15 minutes, two photos with the app camera (drained and refilled), private by default.
- Vacation: either activated in advance (not public while active; published only after the return, which also avoids revealing absence) or recorded afterwards. Vacation skips any length of time without breaking a streak, but vacation weeks never extend it. The profile shows the vacation share, so implausible amounts are visible; there is no yearly vacation budget. Later leaderboards rank by how complete a streak is (DRAFT-14); a criterion against permanent vacation is worked out later.
- Care sponsor (a neighbour or parent who takes the photo on behalf of a teenager): later feature, DRAFT-16.
- Proposal of the main session, not yet confirmed: a week with a water change but without evidence neither extends nor breaks the streak; a week without any water change breaks it unless it is a vacation.

- Week without evidence (decided by the project owner, 2026-10-16): a week with a water change but without evidence does not break the water change streak.
- Evidence (decided by the project owner, 2026-10-16): the completed guided water change mode with its timer is the evidence; photos are an optional fun extra that creates content, not a requirement.

### Decided by the project owner (2026-10-16), water change streak
- The streak counts in the rhythm periods of the tank's own care plan, not in calendar weeks: a member with a 14-day plan keeps it with one water change per period.
- The plan is a latest date, not a rigid rhythm: each water change starts the next period; a grace of one third of the rhythm, rounded up to whole days, is allowed (14 days: up to 19 days). Two water changes right after each other are not needed.
- Evidence: a photo taken in the app during the guided mode is required for a verified period. A water change without a photo (guided mode without photo, or logged outside the mode, for example while getting used to the app) can keep the streak only by using a freeze. About 2 freezes per year; earning extra freezes through a year of good photo evidence is to be worked out. Vacation (pause) stays separate from freezes.
- This supersedes the earlier notes that photos are optional for the evidence and that a week without evidence never breaks the streak.

- Confirmed by the project owner (2026-10-16): the grace is one third of the rhythm rounded up (7 days: up to day 10, 14 days: up to day 19). A missing photo with no freeze left breaks the streak like a missed period. Manipulated clients are out of scope; device attestation is not wanted (dependency on platform services) unless it becomes cheap.

### Current rules (consolidated 2026-10-16; these replace every earlier rule above)
- Two streaks per tank, side by side; the care heatmap is separate and independent.
- Time-lapse streak: one photo per ISO week taken with the app camera into the time-lapse series of the tank (TASK-5.11); library pictures do not count. Deleting a photo that keeps the streak asks first.
- Water change streak: counts in periods of the tank's care plan rhythm. A period is kept by a water change at the latest rhythm plus grace after the previous one (grace = one third of the rhythm rounded up to whole days: 7 days up to day 10, 14 days up to day 19); each water change starts the next period. A kept period is verified when the water change comes from the guided mode with verified evidence (TASK-5.13, at least one qualifying in-app photo). A water change without verified evidence keeps the period only by using a freeze; without a freeze left the streak breaks like a missed period.
- Freezes: 2 per calendar year (a configuration value); earning extra freezes is worked out later.
- Vacation: activated in advance or recorded afterwards; it pauses both streaks for any length without breaking them, and vacation periods never extend a streak. An active vacation is never visible to others; after the return the past vacation becomes visible; the profile shows the vacation share.
- No freeze-free bridging other than vacation. Leaderboards, reputation and ranking are later (DRAFT-14).

### Implementation (2026-10-16)
- Domain (pure): WaterChangeStreaks (plan periods with grace, verified or freeze, breaks, rhythm history from the change day), TimeLapseStreaks (ISO weeks with an in-app series photo), Vacations. Tables vacation, streak_freeze, care_plan_rule_change; tank_media.captured_in_app.
- API: streaks of a tank (owner and, per TASK-5.7, viewers who see the stream or profile; viewers never see the paused state, deadline, freezes, last water change or an active vacation), the member's overview for Today, vacations (start, end, record afterwards; at most one open, no overlaps, at most 366 days, under the profile row lock), the vacation share on the profile (ended vacations, last 365 days). Outbox vacation.started, vacation.ended, streak.freeze_used.
- App: streak cards on Today (StreakSlot) and the tank page, vacation mode in Me, vacation share on the profile; the camera marks series photos as taken in the app.
- Security review findings fixed: input validation (client date within one day, offset range), information disclosure of the pause, authorization audit, check order and concurrency of vacation requests.
- Interpretations of the subagent to confirm with the project owner: freezes are 2 per tank and calendar year (not per member); a vacation starting more than 7 days ago is refused when it would repair a broken period (vacation_would_repair); only-me entries count for every viewer's streak numbers; two water changes on one local day count as one; freezes are recorded when the owner reads.
- Not built: the 16-week care heatmap (TASK-5.17) and a switch to turn streaks off (open); a confirmation before deleting a series photo that keeps the streak waits for a delete path (hook keepingPhotoIds).
- Verification: .NET 553/1182 (1 opt-in benchmark skipped)/349 with 0 warnings, client-core 225, mobile 396, both typechecks, all 35 write e2e specs green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Streaks/*, app/api/tests/Cichlids.Infrastructure.Tests/Persistence/StreakConstraintTests.cs, app/client-core/src/streaks.test.ts, vacation.test.ts, app/mobile/src/streaks/streakText.test.ts, app/mobile/e2e/playwright/streaks.write.mjs.

### Decisions of the project owner (2026-10-16)
- Freezes: 2 per tank and tank year. The first tank year starts on the local date the tank was created, every further one on its anniversary (a tank created on February 29 has its anniversary on February 28 in years without that day), so a leap year has one day more. This replaces the calendar year that is built.
- Retroactive vacation: a vacation starting at most 7 days before it is entered may repair the streak, as built; reminders before the break point to it (TASK-5.18).
- Only-me entries count in the streak every viewer sees, as built.
- Several water changes on one local day are one streak step, as built.
- A needed freeze is recorded when the owner reads the streak, as built.
- Streaks can be turned off per member (TASK-5.19).
<!-- SECTION:NOTES:END -->
