---
id: TASK-8.4
title: Return retrospective
status: In Review
assignee: []
created_date: '2026-10-17 09:50'
updated_date: '2026-10-17 12:10'
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
- The archive rule sits behind IArchiveRetrospectiveGate (IsDueAsync(profile)); the registered implementation answers true for a legacy member (profile.legacy_id) with archive content who has not seen the archive retrospective.
- The window of the cards is from away_since (due) or from return_retrospective_since (seen within 30 days) until now.

### Cards (all built from what the member may see, no counts of hidden content)
- Overdue care: CarePlanService.ListDueAsync filtered to the overdue state, at most 10 items.
- Responses: positive comment votes of other members on own comments since the bound, and comments of other members on own published stories and live tanks since the bound (content the owner always sees).
- Followed tanks: FollowedTanksService with a since-bound (same visibility, batched per kind, at most 5 plus hasMore).
- Own regions: public published tanks of other, not suspended members whose region is in profile.interests (America unassigned counts for both American regions) and published stories pointing only to such public tanks, at most 5 each, newest first.
- Next steps: derived from the cards, only present when a card above exists: tanks with overdue care (log missed care) and own tanks with a time-lapse (guided camera). No archive content.
- No card with data means no retrospective (204).

### API
- GET /api/me/retrospectives/return?date=&utcOffsetMinutes= returns the retrospective (seen flag, since, cards) or 204.
- POST /api/me/retrospectives/return/seen stores return_retrospective_seen_at and return_retrospective_since, clears away_since (one conditional UPDATE, idempotent, no effect when nothing is due).
- Provisional callers get 403 like other registered-only routes.

### App
- client-core mapping to the cards of the shared story presentation; OpenAPI client regenerated.
- Mobile: the first app start of a return (after the onboarding gate and after a due archive retrospective) opens the presentation of the archive retrospective; Me lists an entry while a retrospective is due or seen within 30 days; next steps lead to the care view of the tank (retroactive logging) and the guided camera; texts in all 11 catalogs.

### Tests
- API integration tests: 28-day absence, 91 days, archive wins, no cards is 204, visibility (private, followers, selected, only me, provisional and suspended owners, no hidden counts), seen stores and clears, Me window of 30 days.
- Golden snapshot: both columns nulled, migration pinned.
- client-core and mobile unit tests, one Playwright write spec.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Due and cards
- GET /api/me/retrospectives/return (date and utcOffsetMinutes as the care routes) answers the retrospective or 204. It is due while away_since is set, no return retrospective was seen within 91 days and no archive retrospective is due. After it was seen it stays readable for 30 days, covering the absence it was shown for (profile.return_retrospective_since keeps the away_since; migration ProfileReturnRetrospective adds it and return_retrospective_seen_at). Both columns are private and nulled by the golden snapshot.
- POST /api/me/retrospectives/return/seen stores both columns and clears away_since in one conditional UPDATE; it changes nothing when no absence is recorded, when a retrospective was seen within 91 days or when an archive retrospective is due. Both routes are registered-only (403 for provisional callers).
- IArchiveRetrospectiveGate decides whether the archive retrospective is due: a member with profile.legacy_id and archive content who has not seen the archive retrospective. The legacy_id condition keeps members without legacy data out of the archive precedence, although the archive retrospective itself also counts comments on native content.
- Cards: overdue care (CarePlanService.ListDueAsync, overdue only, at most 10), responses (positive votes on own comments and comments on own published stories and live tanks since the bound, as counts of content the owner sees), followed tanks (FollowedTanksService.GetSinceAsync with the visibility rules of Today, at most 5), news of the own regions (public published tanks and stories pointing only to public tanks in the regions of profile.interests, at most 5 each) and next steps (own tanks with overdue care and own tanks with a time-lapse). Cards without data are null; without any data card there is no retrospective and no next steps. Every list comes from visible data only, no count of hidden content is returned.
- The queries are batched per card; the followed tanks keep the batching of TASK-5.24.

### App
- client-core maps the retrospective to the cards of the shared story presentation (a welcome card with the start of the absence, the data cards, the next steps).
- The presentation of the archive retrospective (RetrospectivePlayer) plays them at /retrospective/return. The app opens it at the first start of a return (OnboardingGate), after the onboarding offer and only when no archive retrospective opens; opening records it as seen. The Me tab shows an entry while a retrospective is due or was seen within 30 days.
- Next steps lead to the care view of the tank (/tanks/{id}?view=care, retroactive logging) and the guided camera (/tanks/{id}/camera). Texts are in all 11 catalogs.

### Verification
- .NET: Api.Tests 2544 passed (1 skipped), Infrastructure.Tests 744, Etl.Tests 351; client-core 368 tests and typecheck; mobile 562 tests and typecheck; golden snapshot tests green against PostgreSQL 18.6 (own container, removed); full write E2E 57 specs green including return-retrospective.write.mjs.
- Tests: Features/Me/ReturnRetrospectiveTests (27: 28-day absence, 91 days, archive precedence, no cards, visibility of private, followers, selected, only-me, provisional and suspended owners, seen, Me window, provisional and anonymous callers), client-core returnRetrospective.test.ts, mobile retrospectiveView.test.ts, e2e return-retrospective.write.mjs.

### Review follow-ups
- GET /api/me/retrospectives/return is not free of side effects: a return retrospective belongs to the first app start of a return. When the read finds a recorded absence (away_since) that it cannot show, because the quarter is blocked or no card has data, it clears away_since in the same request with one conditional update, so the absence lapses and does not open weeks later. A due archive retrospective does not lapse the absence; the return retrospective waits for the next start. A retrospective seen within 30 days stays readable.
- GET /api/me carries returnRetrospective { available, seenAt }, computed from the profile columns alone (due, or seen within 30 days with the covered start set); the Me tab reads it and the full read happens only at app start and when the presentation opens.
- Kudos count only on comments whose story is published and not deleted, or whose tank is not deleted and visible to the member. Comments received count only from guests and from members who are not suspended.
- Verification: Api.Tests 2567 passed (1 skipped), Infrastructure.Tests 744, Etl.Tests 351; client-core 373; mobile 572; return-retrospective and 00-onboarding write specs green.
<!-- SECTION:NOTES:END -->
