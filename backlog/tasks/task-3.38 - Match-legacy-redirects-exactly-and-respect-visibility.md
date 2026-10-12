---
id: TASK-3.38
title: Match legacy redirects exactly and respect visibility
status: To Do
assignee: []
created_date: '2026-10-12 04:48'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
parent_task_id: TASK-3
priority: medium
ordinal: 62000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R29, R30.

Legacy redirect lookups and searches treat % and _ as plain characters and compare aliases exactly, ignoring case. Redirects lead only to visible content; archived, deleted or unpublished pictures and tanks answer 410 Gone.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given the URL /pictures/pic/%25.html, When it is requested, Then the API answers 410
- [ ] #2 Given the legacy URL of an archived picture, When it is requested, Then the API answers 410
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
