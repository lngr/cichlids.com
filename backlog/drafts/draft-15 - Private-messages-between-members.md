---
id: DRAFT-15
title: Private messages between members
status: Draft
assignee: []
created_date: '2026-10-16 06:27'
updated_date: '2026-10-16 06:32'
labels: []
dependencies: []
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal (requested by the project owner, 2026-10-16)
Members write each other private messages with a private inbox, without revealing phone numbers or e-mail addresses. Needed for the relaunch.

### To work out
- Who may message whom (followers only, everyone, provisional accounts), message requests from strangers, blocking and reporting, spam and rate limits, notifications (local, Web Push), retention and deletion, moderation access, encryption at rest, attachments (photos).
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
### Decided by the project owner (2026-10-16)
- Private messages only between members who follow each other (mutual follow).
### Proposal of the main session
- Mutual follow keeps spam out; still needed: blocking (ends the conversation and the mutual follow), reporting a conversation to moderation (only reported messages become visible to moderators), rate limits, notifications.
<!-- SECTION:NOTES:END -->
