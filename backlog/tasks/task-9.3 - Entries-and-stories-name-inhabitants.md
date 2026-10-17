---
id: TASK-9.3
title: Entries and stories name inhabitants
status: To Do
assignee: []
created_date: '2026-10-17 12:36'
labels: []
dependencies:
  - TASK-9.1
parent_task_id: TASK-9
priority: medium
ordinal: 127000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A care entry or a story of a tank names the inhabitants it concerns; the entry then also appears on the timeline of each named fish it names (ADR-0031).

### Scope
- Care entries: an optional list of inhabitants of the same tank, editable with the entry; the named fish timeline lists those entries with the visibility rules of care entries (only-me entries only for the owner).
- Stories: the subject picker of the story editor offers inhabitants of the member's own tanks (story subjects of type inhabitant exist, ADR-0029); a named fish page lists the published stories that name it.
- Visibility follows the tank and the entry or story.

### Reference
- ADR-0029, ADR-0031, TASK-5.6, TASK-5.20.5.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a water change entry naming Bruno, When a viewer opens Bruno's page, Then the entry appears on his timeline
- [ ] #2 Given a story with Bruno as a subject, When a viewer who may see the tank opens Bruno's page, Then the story is listed
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
