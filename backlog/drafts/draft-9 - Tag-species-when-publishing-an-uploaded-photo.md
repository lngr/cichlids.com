---
id: DRAFT-9
title: Tag species when publishing an uploaded photo
status: Draft
assignee: []
created_date: '2026-09-29 05:29'
labels: []
dependencies: []
parent_task_id: TASK-3
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
When publishing an upload, the member optionally tags species from the catalog, so new photos appear in the gallery species filter like migrated ones.

### Context
- Migrated pictures carry 54,039 species links (post_species); the gallery species filter uses them.
- The upload flow publishes without species.

### Scope
- Publish request accepts species ids; API validation and tests.
- App: species search in the publish form (829 catalog entries).
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
