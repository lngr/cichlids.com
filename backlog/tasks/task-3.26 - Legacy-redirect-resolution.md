---
id: TASK-3.26
title: Legacy redirect resolution
status: In Review
assignee: []
created_date: '2026-07-08 19:34'
updated_date: '2026-07-09 02:55'
labels: []
dependencies:
  - TASK-3.5
  - TASK-3.24
parent_task_id: TASK-3
priority: high
ordinal: 46000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Permanent redirects for the indexed legacy URL patterns: /pictures/pic/(alias).html via slug_alias, /tanks/details/(id) and /members/(uid)/... via the legacy_id bridge, /browse/species/(alias).html via species slugs, /disc/read.php?forum,thread,message via discussion legacy ids, /wiki/index.php/(name) best effort onto species pages. Unknown targets answer 410.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given migrated aliases and legacy ids, When a legacy URL of an indexed pattern is requested, Then the response is a permanent redirect to the canonical new URL, and unknown targets answer with status 410
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
301 redirects for /pictures/pic (alias, case fallback), /tanks/details, /members, /browse/species, /disc/read.php and list.php (raw comma query), /wiki/index.php with species fallback chain; 410 for unknown targets. Real smoke with live Location headers.
<!-- SECTION:NOTES:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
