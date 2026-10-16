---
id: TASK-5.20
title: Media links and stories
status: In Review
assignee: []
created_date: '2026-10-16 14:10'
updated_date: '2026-10-16 20:12'
labels: []
dependencies: []
parent_task_id: TASK-5
priority: high
ordinal: 100000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Every photo and video attaches to the places that show it through one link table, with one visibility rule, one placement worker and one signed delivery route, and gallery entries are stories with items, captions, a cover and subjects (ADR-0029, accepted by the project owner, 2026-10-16). Members keep all their files in a media library.

### Decisions of the project owner (2026-10-16)
- Context reference through typed nullable foreign keys plus a context type and an exactly-one check.
- Only the owner of a media item links it; subjects may point to other members' tanks, inhabitants and stories and are checked per viewer.
- Files without links stay in the owner's media library; only deleting from the library removes a file with its renditions and all of its links.
- Avatar and header of a profile are links with context profile.
- A story without a chosen cover shows its first item.
- Quick share to the gallery creates a single-item story once per media item; afterwards the item shows that it is in the gallery with a link to that story. Adding a media item to further stories is possible any number of times.
- A published story is public; a subject pointing to a tank or inhabitant is shown only to viewers who may see it.

### Order
1. TASK-5.20.1 schema and data move (no HTTP contract change).
2. TASK-5.20.2 visibility, placement and delivery.
3. TASK-5.20.3 stories.
4. TASK-5.20.4 media library, TASK-5.20.5 sharing and story editing, TASK-5.20.6 avatar and header.
TASK-5.15 (followers and chosen people) builds on TASK-5.20.2.

### Reference
- ADR-0029, ADR-0020, ADR-0026; TASK-5.7 notes (gallery and tank photos), PROJECT-STATUS O8.
<!-- SECTION:DESCRIPTION:END -->

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
### All steps built (2026-10-16)
- TASK-5.20.1 to 5.20.6 are in review; full suite on main green after the last step: .NET 643/2104 (1 skipped)/350, client-core and mobile green, 48 write e2e specs. The golden snapshot follows every schema step (TASK-5.22).
<!-- SECTION:NOTES:END -->
