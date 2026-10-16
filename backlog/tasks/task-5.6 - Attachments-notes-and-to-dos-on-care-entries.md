---
id: TASK-5.6
title: 'Attachments, notes and to-dos on care entries'
status: In Review
assignee: []
created_date: '2026-10-15 12:26'
updated_date: '2026-10-16 04:16'
labels: []
dependencies: []
references:
  - app/mobile/e2e/playwright/care-attachments.write.mjs
  - app/api/tests/Cichlids.Api.Tests/Features/Care/CarePhotoTests.cs
parent_task_id: TASK-5
priority: medium
ordinal: 86000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
Every care entry can carry photos, a note and a to-do (for example buy water conditioner); open to-dos form a list per member.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given a water change entry, When the member adds a photo, a note and a to-do, Then the entry shows all three and the to-do appears in the to-do list until it is done
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
### Implementation (2026-10-16)
- Note: one plain-text note per care entry (up to 2000 characters, trimmed); a PATCH with only a note changes only the note, a blank note removes it.
- Photos: up to 6 per entry through the shared upload pipeline (formats, hourly limit, variants). Care photos and all their variants live under the private/ storage prefix, outside the public bucket prefixes; the API serves a variant through GET /api/care-photos/{token}, a 30-minute data-protection token for one variant that only the owner-only entry representation issues. Tampered, expired or removed tokens get a coded 404; direct storage requests get 403. Removing a photo or deleting its entry deletes the media rows in the same transaction and the stored objects right after the commit.
- To-dos: on a care entry or only on a tank, open or done with done time; GET /api/me/todos with a state filter; deleting an entry keeps its to-dos on the tank (link cleared, todo.updated). Outbox events care.updated, todo.created, todo.updated, todo.deleted in the same transaction.
- App: each care entry offers Add photo, Note and To-do and shows them; a to-do list (Me tab view, linked from Today with the open count) with check-off and reopening.
- Verification: .NET 337/673 (1 opt-in benchmark skipped)/342 with 0 warnings, client-core 82, mobile 271, both typechecks, all 26 write e2e specs green.
- Tests: app/api/tests/Cichlids.Api.Tests/Features/Care/CareNoteTests.cs, CarePhotoTests.cs, CareTodoTests.cs, app/api/tests/Cichlids.Infrastructure.Tests/MediaStorageKeysTests.cs, app/client-core/src/careAttachments.test.ts, app/mobile/src/tanks/carePhotos.test.ts, app/mobile/src/todos/todoGroups.test.ts, app/mobile/e2e/playwright/care-attachments.write.mjs.
- Not verified: native photo picking and viewer on Android and iOS, review of the translations by native speakers, remote CI.
<!-- SECTION:NOTES:END -->
