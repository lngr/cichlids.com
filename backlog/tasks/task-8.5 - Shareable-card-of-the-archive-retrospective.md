---
id: TASK-8.5
title: Shareable card of the archive retrospective
status: In Review
assignee: []
created_date: '2026-10-17 09:50'
updated_date: '2026-10-17 12:20'
labels: []
dependencies:
  - TASK-8.2
parent_task_id: TASK-8
priority: low
ordinal: 123000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Goal
A portrait card summarises the archive retrospective and can be shared from the app.

### Scope
- Portrait card (9:16) with member since, photo count, comments received, the most viewed photo and the app name; no handle unless the member chooses to show it; no data of other members.
- The app renders the card from the retrospective data and hands it to the share sheet of the operating system (native) or offers a download (web).
- The card uses only data the member's own retrospective shows.

### Reference
- MVP-VISION.md 6.1, TASK-8.2.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Given an archive retrospective, When the member taps share on its last card, Then the app produces a portrait image of the summary and opens the share options
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 At least one Given-When-Then acceptance criterion is specified
- [ ] #2 A failing test was written first, then made to pass (red-green)
- [ ] #3 An automated end-to-end test for the user path runs green
- [ ] #4 Authoritative domain logic is covered by unit/integration tests
- [ ] #5 All CI test suites are fully green
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Approach
- App only: the retrospective cards already hold member since, the photo and comment counts and the most viewed photo; the handle comes from the profile of the member (GET /api/me).
- Card model (pure): shareCardModel builds the fields of the card from the retrospective cards; figures without data and the handle (only when the member switched it on) are left out.
- Card view: portrait 9:16 React Native view with fixed colours (identical in light and dark), logical start/end layout for RTL.
- Rendering and sharing: native renders the visible card view to a PNG with react-native-view-shot and opens the share sheet with expo-sharing; web draws the same model on a 1080x1920 canvas, offers a PNG download and uses the Web Share API with files where the browser supports it. The most viewed photo is fetched with CORS; if that fails the card is drawn without the photo and the sheet says so.
- Entry: a share button on the stats card and on the last card opens a sheet with the card preview, the handle toggle and the share action.
- Texts in all 11 catalogs.

### Tests
- Mobile unit tests for the card model (fields, handle toggle, missing data) and the 9:16 dimensions.
- One Playwright write E2E on web: open the retrospective, tap share, check the PNG download has 9:16 proportions.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implemented
- API: the most viewed card of GET /api/me/retrospectives/archive carries shareImageUrl, a signed path /api/media/photos/{token} (visibility generation of the viewer) for the largest cover variant, whether or not the object lies in the public bucket; null without a cover. Client regenerated; client-core maps and absolutizes it.
- The card model takes member since, photo count, comments received and the most viewed photo with its view count from the cards of the retrospective, and the handle from GET /api/me. The photo comes from shareImageUrl, with the public image as fallback.
- Card: portrait 9:16 (1080 by 1920 pixels, 360 layout units wide), fixed light palette in every theme, content centred vertically, positions counted from the start edge so the layout mirrors for right-to-left. No handle by default; a switch adds it.
- Entry: a share button on the stats card and on the last card opens a sheet with the card preview, the handle switch, a share button and (web) a download button. The player pauses while the sheet is open.
- Native: react-native-view-shot renders the visible card view to a PNG (tmpfile) and expo-sharing opens the share sheet.
- Web: the same layout is drawn on a 1080 by 1920 canvas; the Web Share API with files is used where canShare accepts files, otherwise the PNG is downloaded (cichlids-retrospective.png).
- The photo is fetched with a CORS request. The signed route is on the origin of the API: the web app and the API share one origin in staging and production, and the Development CORS policy covers local servers. If the load fails, the image is made without the photo and the sheet says so.
- Texts in all 11 catalogs (retrospective.share.*).
- The open_tank action of the retrospective actions routes to the tank page; the return retrospective uses it.

### Decisions
- Dependencies: react-native-view-shot 5.1.0 (published 2026-05-03) and expo-sharing 57.0.3 (2026-07-07), both the versions of the Expo SDK 57 bundled list, pinned exactly.
- Web draws on a canvas from the shared layout instead of using view-shot's html2canvas path: no DOM-to-image dependency and the CORS behaviour is explicit.
- A sheet with preview instead of a new route, so the switch has a visible effect and the native capture has a laid-out view.
- Stars received and archive tank data are left off the card (not in the scope list).
- The summary entry is the stats card (the numbers of the retrospective); the Me entry is unchanged.
- The E2E stack serves web and API from two origins and answers without CORS headers, so the spec adds the header to the signed photo route through Playwright (and removes it for the fallback check); the comment in the spec says so.

### Tests
- API: 2563 passed (1 skipped), including two new tests for the share image; Infrastructure 744; ETL 351.
- client-core 374 passed; mobile 591 passed (card model, layout and mirroring, text wrapping, texts per language).
- Write E2E archive-retrospective-share.write.mjs: signed share image in the API response, preview 9:16, no handle by default, handle switch, PNG download 1080 by 1920 with the photo drawn, fallback without photo with a notice, share from the last card.
- Not verified here: the native capture and share sheet (iOS, Android) and the Web Share API with files.
<!-- SECTION:NOTES:END -->
