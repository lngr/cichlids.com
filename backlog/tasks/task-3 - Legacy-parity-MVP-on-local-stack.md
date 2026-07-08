---
id: TASK-3
title: Legacy parity MVP on local stack
status: To Do
assignee: []
created_date: '2026-07-08 18:58'
updated_date: '2026-07-08 19:13'
labels: []
dependencies: []
priority: high
ordinal: 20000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Rebuild the complete legacy feature set on the new stack (ASP.NET Core 10 API, Postgres, Keycloak, RustFS object store, Expo client) with the legacy data migrated, usable end to end on the local compose stack.

### Scope
- New relational domain schema with a legacy_id bridge on every migrated entity
- Idempotent ETL from the legacy MySQL into Postgres
- Media pipeline seeding originals and variants into the object store
- API with full legacy parity (pictures, tanks, comments, ratings, species, users, upload)
- Generated TypeScript client and Expo app core screens
- Local Keycloak auth with Auth0 account import

### Out of scope
- Production deployment (the GitOps app tier is separate work)
- Video transcoding, reputation formulas, care/tracking layer, AI agents
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
### Agreed ETL hygiene rules
- Pictures: non-deleted rows migrate. Gallery pictures (legacy pid 21, 29, 109) become published posts, contest pictures (131) become posts with topic contest, unpublished uploads stay drafts. Section pictures (62, 63, 137) become tank media, profile and avatar pictures (138, 139) attach to profiles. Deleted pictures are not migrated and are counted in the verification report.
- Orphaned content (owners hard-deleted in the source, anonymous uploads): migrated under unlisted placeholder profiles; the resulting posts get state archived, excluded from all public queries and reserved for later agent-driven reseeding.
- Users: only accounts with content (any picture, tank or comment) get a profile. Contentless accounts are not migrated; login continuity comes from the identity provider import alone.
- Comments: split into comment (text) and rating (stars) rows sharing the legacy key. Comments whose target is missing or not migrated go to the archive vault (Postgres schema archive) with the full source row as JSONB for later reuse.
- Comment votes migrate with deduplication per comment and member.
- Tanks: non-deleted tanks migrate, including tanks without media; the legacy not_shown flag is ignored.
- Galleries: non-deleted galleries convert to collections; hidden galleries become non-public collections.
<!-- SECTION:NOTES:END -->
