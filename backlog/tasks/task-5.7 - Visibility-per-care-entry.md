---
id: TASK-5.7
title: Visibility per care entry
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 08:43'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/visibility.write.mjs
  - >-
    app/api/tests/Cichlids.Api.Tests/Features/Tanks/TankVisibilityReadPathTests.cs
parent_task_id: TASK-5
priority: medium
ordinal: 87000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Each care entry has a visibility; private entries never leave the owner, public entries appear in the tank timeline for others.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a private and a public entry of a tank, When another member opens the tank, Then they see only the public entry
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
### Open: default visibility (project owner, 2026-10-16)
- The default must fit the feed idea of the product. Proposals so far (private by default, per-tank setting, public without exact times) do not fit it in the view of the project owner; the decision is deferred.
- Considerations to weigh: exact timestamps reveal absence patterns; data protection by default (GDPR Art. 25(2)); timelines that live from care entries.
- Until the decision only the tank owner reads care entries (as implemented in TASK-5.1). Do not implement this story before the decision.

### Visibility concept (decided by the project owner, 2026-10-16)
- No delayed or partial stream: a visible stream is complete. The burglary concern does not apply (the address of a member is not known from the app).
- Levels per tank, as in other social networks: public (everyone sees it and can follow), followers (follow requests the owner approves; only approved followers see it), selected members (the owner names them, for example family and friends), private (only the owner).
- Two layers per tank: the tank profile (header, photos, time-lapse, story) and the care stream. The stream inherits the level of the profile; it can be restricted further (for example public profile, stream only for selected members) but never opened wider than the profile.
- There is no default: creating a tank (also in the onboarding) asks first whether the tank should be public, with an explanation that others can see it and follow it, and two equal buttons without preselection. Yes makes the profile and the stream public and is stored with its time as consent; no makes the tank private. The finer levels are set afterwards in the prominently reachable tank settings.
- The owner always sees everything of their own tanks.
- Open details, asked one by one: legacy tanks of returning members (the archive was public), tanks created before this question existed, hiding single entries of a public stream, following without an account.

- Legacy tanks (decided by the project owner, 2026-10-16): archive tanks stay public by default, including following; the legacy site only knew public tanks and had no care stream.

- Tanks created before the visibility question existed (decided by the project owner, 2026-10-16): they stay public; the question applies to newly created tanks.

- Single entries (decided by the project owner, 2026-10-16): each care entry can be set to only me as an exception; otherwise it follows the level of the stream.

- Without an account (decided by the project owner, 2026-10-16): public tanks with their stream are visible without an account; following, kudos and comments need an account. The teaser cut of the web stays its own planning.

- Implementation hint: tank photos (including the time-lapse section of TASK-5.11) use public variant URLs; photos of tanks that are not public must move to the private storage prefix with owner- or audience-checked delivery, as the care photos of TASK-5.6 do.

- The open note above is resolved by the decided concept and details; the story is ready.
- Story cut: TASK-5.7 covers the levels public and private for profile and stream, only me per entry, the question on creating a tank, the enforcement in every read path and the private delivery of photos of tanks that are not public; the levels followers (approved follow requests) and selected members are TASK-5.15.

### Implementation (2026-10-16)
- Domain: TankVisibilityRules decides for owner, member and anonymous viewers (profile, stream, entry with only me, stream within profile, CanFollow); levels public and private with an enum ready for TASK-5.15. Migration: existing tanks stay public without a consent row.
- Creating a tank requires isPublic (no default) and stores the consent with its time; tank settings change the levels (owner only, tank.visibility_changed, widening the stream beyond the profile is refused).
- Read paths: tank list, detail, time-lapse, videos, comments; profile tank lists and counts; care entries, series, plan, to-dos, care photos and videos; Discover (one shared public-tank query for computing and the read-time filter, including pictures of tank-linked posts), Today, legacy redirects. Hidden items answer a coded 404.
- Media: URLs follow the tank's profile level, never the storage location; media of a tank that is not public only get signed paths. The tank row carries the placement state; a worker moves variants and HLS directories between the public and private prefix (copy, repoint, delete the other side), retries with growing delays, logs failures, and clears the pending mark only after a check under the tank row lock; a tank that turns private waits up to 10 seconds for the immediate move. Series photo uploads and transcoding finishes re-check the level under the tank row lock. Public variants carry no Cache-Control header; a cache in front of the bucket can keep serving a copy after deletion (documented trade-off for the cache configuration).
- Security review findings fixed: Discover visibility parity, fail-open media placement, races of series photos and transcoding with a visibility change.
- Open question to the project owner (2026-10-16): gallery posts linked to a private tank still appear in the gallery feed; proposal: a post follows the visibility of its tank.
- Verification: .NET 542/1061 (1 opt-in benchmark skipped)/349 with 0 warnings, client-core 198, mobile 382, both typechecks, all 34 write e2e specs green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Tanks/TankVisibility*Tests.cs, TankMediaPlacementTests.cs, TankMediaFailClosedTests.cs, Features/Care/CareOnlyMeTests.cs, Features/Discover/DiscoverVisibilityTests.cs, Features/Me/TodayVisibilityTests.cs, app/mobile/e2e/playwright/visibility.write.mjs.
- Still in progress: the legacy takeover keeps archive tanks public without the question.

- Legacy takeover done: the onboarding shows a member's archive tanks right after yes without the question; the adopted tank stays public in profile and stream without a consent row; the question appears when a new tank is chosen.
- Verification after the follow-ups: .NET 542/1062 (1 opt-in benchmark skipped)/349 with 0 warnings, client-core 198, mobile 384, both typechecks, all 34 write e2e specs green. A single run of visibility.write.mjs once timed out at care-only-me-badge and passed on rerun (small flake to watch).

- Gallery posts linked to a tank (project owner, 2026-10-16): a photo is either part of a tank or posted in the public gallery independently of a tank; the mixed form of the current model (a gallery post with a tank link) does not match the concept of the project owner and is discussed again (PROJECT-STATUS O8). The behavior stays unchanged until then; the proposal that a post follows the visibility of its tank is withdrawn.
<!-- SECTION:NOTES:END -->
