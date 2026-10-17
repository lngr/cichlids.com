---
id: TASK-8.4
title: Return retrospective
status: To Do
assignee: []
created_date: '2026-10-17 09:50'
updated_date: '2026-10-17 10:39'
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

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
- Migration ProfileReturnRetrospective adds profile.return_retrospective_seen_at and profile.return_retrospective_since (the away_since at the time of seeing, so Me can open the same retrospective for 30 days after away_since is cleared). Both are private to the member.
- ReturnRetrospectiveService (Features/Me) decides due-ness and builds the cards. Due: away_since set, no return retrospective seen within 91 days, and no archive retrospective due.
- The archive rule sits behind IArchiveRetrospectiveGate (IsDueAsync(profile)). Until the archive retrospective exists, the registered gate answers false; it is replaced by the rule "legacy_id set and archive retrospective not seen" when the column of the archive retrospective exists.
- The window of the cards is [since, now] where since is away_since (due) or return_retrospective_since (seen within 30 days).

### Cards (all built from what the member may see, no counts of hidden content)
- Overdue care: CarePlanService.ListDueAsync filtered to the overdue state, at most 10 items.
- Responses: positive comment votes of other members on own comments since the bound, and comments of other members on own published stories and live tanks since the bound (counts of content the owner always sees).
- Followed tanks: FollowedTanksService gets a since-bound overload (same visibility, batched per kind, at most 5 plus hasMore).
- Own regions: public published tanks of other, not suspended members whose region is in profile.interests (America unassigned counts for both American regions) and published stories pointing only to such public tanks, at most 5 each, newest first.
- Next steps: derived from the cards, only present when a card above exists: tanks with overdue care (log missed care) and own tanks with a time-lapse (guided camera). No archive content.
- No card with data means no retrospective (204).

### API
- GET /api/me/retrospectives/return?date=&utcOffsetMinutes= returns the retrospective (seen flag, since, cards) or 204.
- POST /api/me/retrospectives/return/seen stores return_retrospective_seen_at and return_retrospective_since, clears away_since (one conditional UPDATE, idempotent, no effect when nothing is due).
- Provisional callers get 403 like other registered-only routes.

### App
- client-core mapping, OpenAPI client regenerated.
- Mobile: first app start of a return (after the onboarding gate and after a due archive retrospective) opens the story presentation of the archive retrospective; Me lists an entry for 30 days; next steps lead to retroactive logging and the guided camera; texts in all 11 catalogs.

### Tests
- API integration tests: 28-day absence, 91 days, archive wins, no cards is 204, visibility (private, followers, selected, only me, provisional and suspended owners, no hidden counts), seen stores and clears, Me window of 30 days.
- Golden snapshot: both columns nulled, migration pinned.
- client-core and mobile unit tests, one Playwright write spec.
<!-- SECTION:PLAN:END -->
