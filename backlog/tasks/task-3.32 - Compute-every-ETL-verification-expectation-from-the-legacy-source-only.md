---
id: TASK-3.32
title: Compute every ETL verification expectation from the legacy source only
status: In Review
assignee: []
created_date: '2026-10-12 04:48'
updated_date: '2026-10-12 08:44'
labels: []
dependencies: []
references:
  - REVIEW-MVP.md
  - app/api/tests/Cichlids.Etl.Tests/VerifyStepTests.cs
  - app/api/tests/Cichlids.Etl.Tests/VerifyEtlFixture.cs
  - app/api/tests/Cichlids.Etl.Tests/VerificationReportPrinterTests.cs
  - app/api/tests/Cichlids.Etl.Tests/RatingSelectionTests.cs
  - app/api/tests/Cichlids.Etl.Tests/LegacyLocalTimeTests.cs
  - app/api/tests/Cichlids.Etl.Tests/CommentMigrationStepTests.cs
parent_task_id: TASK-3
priority: high
ordinal: 54000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Decisions: REVIEW-MVP.md, R34, with the verify parts of R9, R16 and R19.

The verify step derives all expectations from the legacy database, expects no API-created rows, and names for each comparison the migration rule behind it. It covers the agreed rules: one rating per member and target with self-ratings dropped, empty galleries skipped, vote timestamps converted from America/New_York.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Given a target where the operator deleted one migrated post, When the verify step runs, Then it reports a post mismatch and the comment comparison is not silently adjusted
- [x] #2 Given a correct import, When the verify step runs, Then every comparison passes and names its rule in the report
- [x] #3 Given the imported votes, When the operator queries for a vote older than its comment, Then there is none
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
- VerifyStep computes every expectation from the legacy MySQL only. It no longer loads migrated ids from Postgres (VerifyStep.cs:31-33); it derives the post, tank and species sets with the documented filters (pictures not deleted with pid IN (21, 29, 109, 131), tanks deleted = 0, species deleted = 0).
- Actual values stay whole-table counts, so any API-created row is a mismatch (R3: verify runs right after the import into an empty target). profiles_member counts every member profile; profiles_member_registered is removed.
- Every VerificationRow carries a mandatory Rule sentence, printed in the console table and written as "rule" in the JSON.
- New comparisons: ratings per R9 (one per member and target, newest wins, ties by the higher legacy uid, self-ratings dropped, anonymous ratings with fe_user = 0 kept unchanged: never collapsed, never self) with legacy-derived info rows for collapsed and dropped counts; the R19 invariant "no vote older than its comment"; an R16 info row for skipped empty galleries.
- This task also implements the ETL side of R9 (dedupe, self-drop, counters) and R19 (vote timestamps read as America/New_York and converted to UTC), because verify cannot pass otherwise.
- Self-rating detection is independent: the ETL compares target profile ids (post or tank author against the resolved rater), verify compares legacy fe_user with the owner fe_user.
- The verify tests run the pipeline once per isolated database (TASK-3.30 helper and EtlRunner.RunAllAsync) and mutate the target only inside rolled-back transactions.

### Steps
1. Rule column in the report.
   - Failing test first: unit test VerificationReportPrinterTests expects "rule" per JSON row; VerifyStepTests case EveryRowNamesItsRule asserts a non-empty Rule on every row.
   - Production: app/api/src/Cichlids.Etl/Runtime/VerificationRow.cs (required Rule, checked non-empty in Compare and Info), Runtime/VerificationReportPrinter.cs (rule line per row, rule in JsonRowDto), new Runtime/MigrationRules.cs (catalog of one-sentence rules matching the TASK-3 hygiene rules), Steps/VerifyStep.cs (every row passes a rule). No inline tags in doc comments (R43).
2. Test harness on the TASK-3.30 helper (no behaviour change).
   - app/api/tests/Cichlids.Etl.Tests/VerifyEtlFixture.cs: create the isolated database with the TASK-3.30 helper and run EtlRunner.RunAllAsync once per fixture.
   - Add VerifyInRolledBackTransactionAsync(mutate): begin a transaction, set context.Db.Database.UseTransaction and context.Transaction, run new VerifyStep().RunAsync, roll back.
   - Move the existing VerifyStepTests cases onto the helper.
3. Legacy-only expectations (R34, AC #1).
   - Failing tests first: in a rolled-back transaction delete post legacy 9101 with its dependents (comment_vote on its comments, comment, rating, slug_alias, post_species, collection_entry, then post; post_media cascades). Assert post mismatch (expected 2, actual 1), comment row keeps its legacy expectation and is a mismatch, vault_legacy_comment keeps its expectation. Invert IgnoresAMemberProfileTheApiCreatedForAFirstLoginWhenCountingMigratedMembers (VerifyStepTests.cs:108-150) into a test that flags an API-created member profile.
   - Production (Steps/VerifyStep.cs): replace LoadLegacyIdSetAsync (:31-33, :718-731) with legacy loaders for eligible picture uids with owner fe_user, tank uids with owner fe_user and species uids; profiles_member counts kind = 'member' and the registered row goes (:130-139); add info row identities_oidc (created by the account import, not the ETL); rewrite the class comment (:9-16).
4. Vote timestamps (R19, AC #3).
   - Failing tests first: new LegacyLocalTimeTests (winter, summer, 2016-03-13 01:59:59 and 03:00:00, a time in the spring gap shifted one hour forward, ambiguous 2016-11-06 01:30 read as standard time). Fixtures/verify-legacy-seed.sql: a vote on comment 9303 (17:22 EST) by 9010 at local 2023-11-14 17:30:00. VerifyStepTests: comment_vote_not_before_comment expected 0, comment_vote expected 2. CommentMigrationStepTests: assert the converted created_at of one vote.
   - Production: new Runtime/LegacyLocalTime.cs (TimeZoneInfo America/New_York, gap shifted forward, ambiguous as standard time); Steps/CommentMigrationStep.cs:447 uses it; VerifyStep adds the invariant row (vote joined to comment where v.created_at < c.created_at).
5. One rating per member and target in the ETL (R9).
   - Failing tests first: new pure RatingSelectionTests (newest wins, tie goes to the higher uid, fe_user 0 never collapses and is never self, self-ratings dropped). Adjust Fixtures/legacy-seed.sql and CommentMigrationStepTests.cs: today every fixture rating is by legacy 400 on post 4001 and tank 4101 owned by 400, so re-attribute the raters so the 5001, 5002 and 5009 assertions keep holding, add an older duplicate that collapses and an owner rating that is dropped; assert the skip counters rating_collapsed_older and rating_self_dropped.
   - Production: new Steps/RatingSelection.cs (legacy pre-pass over non-deleted rated rows returning the winner uids and the collapsed count); CommentMigrationStep.cs:184-195 emits a rating only for winners whose resolved rater differs from the target author (load post.author_profile_id and tank.profile_id next to :45-46). The comment text always stays.
6. Rating comparison in verify (R9, AC #2).
   - Failing test first: verify-legacy-seed.sql adds 9310 (older rating by 9011 on 9101, collapsed), 9311 (9011 on own tank 9201, self), 9312 (anonymous rating on anonymous tank 9202, kept). Expect rating 2 (9301, 9312), info rating_collapsed 1, info rating_self_dropped 2.
   - Production: VerifyStep.AddCommentRatingVaultRowsAsync (:489-568) implements the rule on its own from legacy owner maps (no shared code with RatingSelection) and adds both info rows.
7. Empty galleries (R16 verify part).
   - Failing test first: seed gallery 9502 without mm rows; expect info collection_skipped_empty 1 while collection stays 1.
   - Production: VerifyStep.AddCollectionRowsAsync (:647-683) adds the legacy-derived info row whose rule names the exception.
8. Docs and backlog (backlog CLI only): TASK-3 hygiene rules get the rating rule, the anonymous-rating rule, the vote timezone and the empty-gallery exception; app/stack/README.md notes that verify expects an import into an empty target.

### Verification
- AC #1: automated by the step 3 test. Operator on the dev stack after reset and full run: delete one post with its dependents in psql, run etl verify; post shows mismatch -1, comment shows a mismatch with the expected value unchanged from the clean run; reset afterwards.
- AC #2: automated full-pipeline test with zero mismatches plus EveryRowNamesItsRule. Operator: reset, full run, etl verify --out /tmp/v.json prints "Result: ok"; jq '[.rows[]|select((.rule//"")=="")]|length' /tmp/v.json returns 0. Expected real figures: rating 1,252,380, rating_self_dropped 14,952, rating_collapsed 30,016, collection 161, collection_skipped_empty 86.
- AC #3: automated by the step 4 tests. Operator: SELECT COUNT(*) FROM comment_vote v JOIN comment c ON c.id = v.comment_id WHERE v.created_at < c.created_at; returns 0 (4,838 legacy votes precede their comment when read as UTC).
- CI: Cichlids.Etl.Tests fully green.

### Risks
- Fixture churn in CommentMigrationStepTests and verify-legacy-seed.sql; many hard-coded counts change.
- An ETL image without tzdata makes FindSystemTimeZoneById throw; CI and the devcontainer have it.
- Rule wording can drift from the TASK-3 notes; keep one catalog.
- The rating winner set holds about 1.25M uids in memory, which is acceptable.
- Size L, about 8 commits, 2.5 to 3 days.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Implementation
- VerifyStep computes every expected count from the legacy MySQL only: eligible pictures with owner (not deleted, pid 21, 29, 109, 131), tanks with owner (deleted = 0) and species (deleted = 0). It reads no migrated ids from Postgres. Actual values are whole-table counts, so a row the import did not write is a mismatch.
- profiles_member counts every member profile. identities_oidc is an information row for the links the Keycloak account import writes.
- Every VerificationRow has a mandatory, non-empty Rule (checked in the record itself). The console table prints a rule line under each row, the JSON has a rule field. The sentences live in Runtime/MigrationRules.cs.
- Ratings (ETL): Steps/RatingSelection.cs drops self-ratings, then keeps the newest rating per member and target (tie: higher legacy uid); fe_user 0 ratings always migrate. CommentMigrationStep detects self-ratings by target profile ids (rater profile equals post author or tank owner) and reports the skip counters rating_collapsed_older and rating_self_dropped. The comment text of a dropped rating migrates.
- Ratings (verify): an independent count from legacy owner fe_user maps, the check rating_not_newest (every migrated rating is an anonymous rating or the newest legacy rating of its member and target, expected 0), plus information rows rating_collapsed and rating_self_dropped.
- Vote times: Runtime/LegacyLocalTime.cs reads the vote DATETIME as America/New_York (spring gap shifted one hour forward, ambiguous autumn hour read as standard time). Verify adds comment_vote_not_before_comment (expected 0).
- Empty galleries: verify adds the legacy-derived information row collection_skipped_empty. The step counter and class comment of GalleryMigrationStep belong to TASK-3.39.
- TASK-3 notes list the rating, anonymous-rating, vote-time and empty-gallery rules; app/stack/README.md states that verify expects a target holding only the import.

### Deviation from the plan
- Self-ratings are dropped before the newest rating is chosen. This matches the expected real figures below (14,952 self-ratings are rows, 30,016 collapsed among the remaining member ratings). Choosing the newest rating first gives the same rating total, but other counters (10,275 and 34,693).

### Tests
- VerifyEtlFixture runs EtlRunner.RunAllAsync once into an isolated database; VerifyInRolledBackTransactionAsync mutates and verifies inside a transaction it rolls back.
- AC 1: VerifyStepTests.FlagsADeletedPostWithoutAdjustingTheCommentExpectations (post 2 vs 1, comment 3 vs 1, vault 3 vs 3).
- AC 2: VerifyStepTests.ReportsNoMismatchesAfterAFullPipelineRun (zero mismatches, all rows asserted) and EveryRowNamesItsRule; VerificationReportPrinterTests.
- AC 3: LegacyLocalTimeTests (DST change days), CommentMigrationStepTests (converted vote time) and the verify invariant row with a seed vote that precedes its comment when read as UTC.
- VerifyStepTests.FlagsAMigratedRatingThatIsNotTheNewestOfItsMember swaps a migrated rating to the collapsed legacy uid and expects a rating_not_newest mismatch.
- RatingSelectionTests cover newest wins, tie by uid, anonymous ratings and self-ratings; CommentMigrationStepTests cover the collapsed and the self-rating counters.
- Full backend suite green: Infrastructure 113, Api 219, Etl 299; Cichlids.Etl.Tests 300 with the newest-rating check.

### Operator checks (dev stack, not automated)
- AC 1: after reset and full run, delete one post with its dependents in psql (comment_vote of its comments, comment, rating, slug_alias, post_species, collection_entry, then post), run etl verify: post shows mismatch -1, comment shows a mismatch with the expected value of the clean run. Reset afterwards.
- AC 2: reset, full run, etl verify --out /tmp/v.json prints Result: ok; jq '[.rows[]|select((.rule//"")=="")]|length' /tmp/v.json returns 0.
- Expected real figures: rating 1,252,380, rating_self_dropped 14,952, rating_collapsed 30,016, collection 161, collection_skipped_empty 86. The rating figures were confirmed read-only against the legacy MySQL.
- AC 3: SELECT COUNT(*) FROM comment_vote v JOIN comment c ON c.id = v.comment_id WHERE v.created_at < c.created_at; returns 0. Read as UTC, 4,838 legacy votes precede their comment; after the New York conversion none does (checked against the legacy data).
- An ETL image needs tzdata for America/New_York; CI and the devcontainer have it.
<!-- SECTION:NOTES:END -->
