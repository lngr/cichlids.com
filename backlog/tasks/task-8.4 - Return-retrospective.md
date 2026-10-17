---
id: TASK-8.4
title: Return retrospective
status: To Do
assignee: []
created_date: '2026-10-17 09:50'
labels: []
dependencies:
  - TASK-8.1
  - TASK-5.24
parent_task_id: TASK-8
priority: medium
ordinal: 122000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A member who was away for four weeks or longer sees what happened in the meantime when returning, at most once per quarter.

### Content
- Overdue care of the own tanks (from the care plan), with the offer to log missed care afterwards (TASK-5.16).
- Kudos received on own comments and comments received on own stories and tanks since away_since.
- News from followed tanks (the query of TASK-5.24, from away_since) and from the own regions (new public tanks and stories in the regions of profile.interests).
- Next steps: log missed care, take a current photo for the time machine (guided camera of TASK-5.11) for a tank with a time-lapse.
- No archive content (no adoption) and cards without data are left out; with no card at all there is no retrospective.

### Behaviour
- Due when away_since (TASK-8.1) is set and no return retrospective was shown in the last 91 days. GET /api/me/retrospectives/return returns the cards or null; POST .../seen stores profile.return_retrospective_seen_at and clears away_since.
- An archive retrospective that is due wins; the return retrospective waits for the next due return.
- The app opens it at the first app start of the return in the same presentation as the archive retrospective; Me lists the latest one for 30 days.
- Visibility: only what the member may see (TASK-5.7, TASK-5.15, ADR-0029), never counts of hidden content.

### Reference
- MVP-VISION.md 6.1, TASK-8.1, TASK-5.24.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a member away for 30 days with overdue care and news from a followed tank, When the member opens the app, Then the return retrospective shows the overdue care, the news and the next steps
- [ ] #2 Given a member who saw a return retrospective 40 days ago, When the member returns after another absence of 4 weeks, Then no return retrospective opens
- [ ] #3 Given news of a tank the member may not see, When the return retrospective loads, Then neither the news nor a count of it appears
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
