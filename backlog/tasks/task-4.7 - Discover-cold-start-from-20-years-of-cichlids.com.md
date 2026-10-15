---
id: TASK-4.7
title: Discover cold start from 20 years of cichlids.com
status: In Review
assignee: []
created_date: '2026-10-15 08:46'
updated_date: '2026-10-15 11:59'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/discover.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Discover/DiscoverTests.cs
parent_task_id: TASK-4
priority: medium
ordinal: 78000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Discover presents the archive: hall of fame per region, tank of the week from the archive,
showcase time-lapses; activity sections (new tanks, active members, counters) exist behind the
feature flag.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an environment with the archive and the activity flag off, When a member opens Discover, Then they see the hall of fame per region, the tank of the week and the showcase time-lapses, and no activity sections
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
### Regions (decided by the project owner, 2026-10-15)
- Lake Malawi, Lake Tanganyika, Lake Victoria, Africa other (West Africa, rivers, Madagascar),
  Central America, South America, Mixed (for example mbuna with haps, community tanks).
- Legacy tanks: malawi, tanganyika map directly; african to Africa other; community to Mixed;
  central_american and south_american directly; legacy "american" without detail becomes
  "Americas (unassigned)", which the member refines with one tap on their tank; until then such
  tanks count for both American regions in the hall of fame.

### Hall of fame, tank of the week, showcase time-lapses (decided by the project owner, 2026-10-15)
- Hall of fame per region: the 20 tanks and 20 pictures with the best weighted (Bayesian) rating
  average; at least 10 ratings; self-ratings are already excluded (TASK-3.36).
- Tank of the week: each week another tank of the regional hall of fame, in rotation.
- Showcase time-lapses: tanks whose photos span at least one year with at least 8 photos from
  different months; the best rated of those.
- View counts do not count (distorted by crawlers in the legacy data); tanks and pictures of
  deleted or suspended members are excluded.

### Region of a picture (decided by the project owner, 2026-10-15)
- No rated picture of the archive is linked to a tank; 21,704 of 53,338 pictures with at least 10 ratings carry an identified species.
- A picture belongs to the region of its linked tank, otherwise to the region of its identified species (species category: lake malawi, lake tanganyika, african to Africa other, american to both American regions).
- Pictures without tank and species appear in no regional list. Discover additionally shows a cross-region top 20 of pictures in which every eligible picture counts.

### Implementation (2026-10-15)
- GET /api/discover (anonymous, optional regions filter) returns per selectable region the hall of fame of tanks and pictures (top 20, Bayesian average with the prior mean of all eligible ratings and weight 10, at least 10 ratings, self-ratings excluded), the tank of the week (rotation through the regional hall of fame by ISO week) and the showcase time-lapses (best rated tanks meeting the showcase rule of TASK-4.6), plus the cross-region top 20 of pictures.
- Excluded: view counts, unpublished or deleted items, items of suspended members and non-member profiles. Tanks and species in americas_unassigned count for both American regions.
- Load: one result for all regions, cached for 15 minutes per ISO week with an async single flight; only tanks with at least 8 photos enter the showcase computation. Measured with synthetic data at archive scale (5,000 tanks, 50,000 pictures, 1.3 million ratings): cold 843 ms, warm about 5 ms for every region set. The opt-in benchmark DiscoverArchiveScaleTests does not run by default.
- Local dev data, candidates before the top 20 limit: Lake Malawi 37 tanks and 11,289 pictures, Lake Tanganyika 16 and 1,963, Africa other 20 and 629, Central and South America 19 and 6,613 each, Lake Victoria and Mixed none; 49,706 pictures qualify for the cross-region list.
- App: Discover shows region chips (interests first), tank of the week, hall of fame of tanks and pictures, showcase time-lapses and the cross-region pictures with empty states per region; the activity sections stay behind discover-activity.
- Verification (state after the load change): .NET 492/325/342 with 0 warnings, client-core 27, mobile 242, both typechecks, all 20 write e2e specs green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Discover/DiscoverTests.cs, DiscoverRankingTests.cs, DiscoverPerformanceTests.cs, app/client-core/src/discover.test.ts, app/mobile/src/discover/regions.test.ts, app/mobile/e2e/playwright/discover.write.mjs.
- Not verified: native Android and iOS runs, remote CI, measurement in production.

- Moderation takes effect at once: the cached rankings are filtered on every request against the current visibility (published, not deleted, owner a member and not suspended) with two small queries; the tank of the week rotates through the visible hall of fame (test CachedArchive_HidesItemsThatBecameInvisibleAfterTheComputation).
<!-- SECTION:NOTES:END -->
