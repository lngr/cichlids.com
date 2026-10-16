---
id: TASK-5.7
title: Visibility per care entry
status: To Do
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 05:26'
labels: []
dependencies: []
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
<!-- SECTION:NOTES:END -->
