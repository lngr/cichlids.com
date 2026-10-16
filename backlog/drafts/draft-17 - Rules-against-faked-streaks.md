---
id: DRAFT-17
title: Rules against faked streaks
status: Draft
assignee: []
created_date: '2026-10-16 12:44'
labels: []
dependencies: []
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Find rules that keep public streaks trustworthy when members could fake them, and decide on them once such cases actually occur (raised by the project owner, 2026-10-16). Until then only-me entries count for the streak every viewer sees (TASK-5.5).

### Concerns
- Entries set to only me escape public control: a member could log every water change as only me and keep the streak without anyone seeing evidence.
- Faked photos and implausible vacation patterns (for example 30 days of vacation every month) make a profile suspicious.

### Ideas to evaluate
- Above a certain share of only-me entries the public streak no longer applies; the streak is then a private motivation shown to its owner only.
- Members can flag a streak as suspicious. When enough members do, the owner is asked to publish evidence again (for example the next water change with a public photo) before the public streak counts again; a tiebreaker decides close cases.
- Suspicious patterns (vacation share, only-me share, reused or implausible photos) lower the trust shown on the profile; relates to reputation (DRAFT-13).

### Reference
- TASK-5.5, TASK-5.7 (only-me entries), TASK-5.13 (evidence), DRAFT-13, DRAFT-14.
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
