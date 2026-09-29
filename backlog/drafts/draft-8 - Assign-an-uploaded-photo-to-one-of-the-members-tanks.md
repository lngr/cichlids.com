---
id: DRAFT-8
title: Assign an uploaded photo to one of the member's tanks
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
When publishing an upload, the member optionally picks one of their tanks; the photo then also appears on that tank's page.

### Context
- Legacy pictures could belong to a tank; the schema has post.tank_id and migrated tank media.
- The upload flow publishes title, description and topic only.

### Scope
- Publish request accepts an optional tank id owned by the caller; API validation and tests.
- App: tank picker in the publish form; tank detail lists the photo.
<!-- SECTION:DESCRIPTION:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->
