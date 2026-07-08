using System.Text.Json;
using Cichlids.Domain.Archive;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(EtlCollection.Name)]
public sealed class CommentMigrationStepTests(EtlFixture fixture)
{
    [Fact]
    public async Task MigratesCommentsRatingsAndVotesIdempotently()
    {
        await RunPrerequisiteStepsAsync();

        var stats1 = await RunStepAsync();

        // uid 1/2 are PictureMigrationStepTests'/TankMigrationStepTests' shared "give this legacy
        // user some content" rows: this step reads user_cichlids_comments unconditionally, and
        // their default type (0) lands them in the unknown_type vault path alongside uid 5006.
        Assert.Equal(15, stats1.Read);
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("comment_deleted"));
        Assert.Equal(3, stats1.SkipReasons.GetValueOrDefault("comment_vaulted_unknown_type"));
        Assert.Equal(2, stats1.SkipReasons.GetValueOrDefault("comment_vaulted_target_deleted"));
        Assert.Equal(2, stats1.SkipReasons.GetValueOrDefault("comment_vaulted_target_missing"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("comment_content_empty"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("comment_rating_from_hidden_comment"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("comment_vote_duplicate"));
        Assert.Equal(2, stats1.SkipReasons.GetValueOrDefault("comment_vote_target_not_migrated"));
        // 7 vault rows + 5 comments + 4 ratings + 2 votes.
        Assert.Equal(18, stats1.Inserted);
        Assert.Equal(0, stats1.Updated);
        Assert.Contains(stats1.Warnings, w => w.Contains("legacy user 402"));
        Assert.Contains(stats1.Warnings, w => w.StartsWith("post rating_average recompute:", StringComparison.Ordinal));

        await using (var db = fixture.CreateTargetContext())
        {
            var owner400 = await db.Profiles.SingleAsync(p => p.LegacyId == 400);
            var owner401 = await db.Profiles.SingleAsync(p => p.LegacyId == 401);
            var post4001 = await db.Posts.SingleAsync(p => p.LegacyId == 4001);
            var tank4101 = await db.Tanks.SingleAsync(t => t.LegacyId == 4101);

            // uid 5001: split into a comment and a rating.
            var comment5001 = await db.Comments.SingleAsync(c => c.LegacyId == 5001);
            Assert.Equal(post4001.Id, comment5001.PostId);
            Assert.Null(comment5001.TankId);
            Assert.Equal(owner400.Id, comment5001.AuthorProfileId);
            Assert.Null(comment5001.PosterName);
            Assert.Equal("Great tank!", comment5001.Body);
            Assert.Null(comment5001.DeletedAt);
            // The legacy score (7) is immediately superseded by this run's own vote-based
            // recompute: two +1 votes (one of them the winner of a duplicate pair) sum to 2.
            Assert.Equal(2, comment5001.Score);

            var rating5001 = await db.Ratings.SingleAsync(r => r.LegacyCommentId == 5001);
            Assert.Equal(post4001.Id, rating5001.PostId);
            Assert.Equal(owner400.Id, rating5001.ProfileId);
            Assert.Equal((short)4, rating5001.Stars);

            // uid 5002: rating-only.
            Assert.False(await db.Comments.AnyAsync(c => c.LegacyId == 5002));
            var rating5002 = await db.Ratings.SingleAsync(r => r.LegacyCommentId == 5002);
            Assert.Equal((short)5, rating5002.Stars);

            // uid 5003: content-empty, neither row exists.
            Assert.False(await db.Comments.AnyAsync(c => c.LegacyId == 5003));
            Assert.False(await db.Ratings.AnyAsync(r => r.LegacyCommentId == 5003));

            // uid 5007: anonymous poster keeps only a display name.
            var anonymous = await db.Comments.SingleAsync(c => c.LegacyId == 5007);
            Assert.Null(anonymous.AuthorProfileId);
            Assert.Equal("Guest Visitor", anonymous.PosterName);

            // uid 5008: legacy user 402 has no migrated profile -- placeholder path.
            var placeholder402 = await db.Profiles.SingleAsync(p => p.LegacyId == 402);
            Assert.Equal(ProfileKind.Archived, placeholder402.Kind);
            var placeholderAuthored = await db.Comments.SingleAsync(c => c.LegacyId == 5008);
            Assert.Equal(placeholder402.Id, placeholderAuthored.AuthorProfileId);

            // uid 5009: hidden with a delete trail; its rating still migrates.
            var hidden = await db.Comments.SingleAsync(c => c.LegacyId == 5009);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000090), hidden.DeletedAt);
            Assert.Equal("spam", hidden.DeleteReason);
            Assert.Equal(owner401.Id, hidden.DeletedByProfileId);
            var hiddenRating = await db.Ratings.SingleAsync(r => r.LegacyCommentId == 5009);
            Assert.Equal((short)2, hiddenRating.Stars);

            // uid 5010: tank-targeted split.
            var tankComment = await db.Comments.SingleAsync(c => c.LegacyId == 5010);
            Assert.Equal(tank4101.Id, tankComment.TankId);
            Assert.Null(tankComment.PostId);
            var tankRating = await db.Ratings.SingleAsync(r => r.LegacyCommentId == 5010);
            Assert.Equal(tank4101.Id, tankRating.TankId);

            // uid 5011: hard-deleted at the top level, never reaches target resolution.
            Assert.False(await db.Comments.AnyAsync(c => c.LegacyId == 5011));
            Assert.False(await db.Ratings.AnyAsync(r => r.LegacyCommentId == 5011));

            // Vault rows.
            Assert.Equal(7, await db.LegacyComments.CountAsync());

            var vaultDeleted = await db.LegacyComments.SingleAsync(v => v.LegacyId == 5004);
            Assert.Equal(LegacyTargetType.Picture, vaultDeleted.LegacyTargetType);
            Assert.Equal(2005, vaultDeleted.LegacyTargetId);
            Assert.Equal("target_deleted", vaultDeleted.VaultReason);
            Assert.Equal(400, vaultDeleted.AuthorLegacyUserId);
            Assert.Equal("orphan comment", vaultDeleted.Body);
            Assert.Equal((short)3, vaultDeleted.Stars);
            using (var payload = JsonDocument.Parse(vaultDeleted.Payload))
            {
                Assert.Equal("orphan comment", payload.RootElement.GetProperty("note").GetString());
                Assert.Equal(2005, payload.RootElement.GetProperty("item").GetInt32());
            }

            var vaultMissing = await db.LegacyComments.SingleAsync(v => v.LegacyId == 5005);
            Assert.Equal(LegacyTargetType.Picture, vaultMissing.LegacyTargetType);
            Assert.Equal("target_missing", vaultMissing.VaultReason);

            var vaultUnknown = await db.LegacyComments.SingleAsync(v => v.LegacyId == 5006);
            Assert.Equal(LegacyTargetType.Picture, vaultUnknown.LegacyTargetType);
            Assert.Equal("unknown_type", vaultUnknown.VaultReason);

            var vaultTankDeleted = await db.LegacyComments.SingleAsync(v => v.LegacyId == 5012);
            Assert.Equal(LegacyTargetType.Tank, vaultTankDeleted.LegacyTargetType);
            Assert.Equal("target_deleted", vaultTankDeleted.VaultReason);

            var vaultTankMissing = await db.LegacyComments.SingleAsync(v => v.LegacyId == 5013);
            Assert.Equal(LegacyTargetType.Tank, vaultTankMissing.LegacyTargetType);
            Assert.Equal("target_missing", vaultTankMissing.VaultReason);

            // Votes: 5 source rows -> 2 valid, 1 duplicate, 2 on rows that never became a comment.
            Assert.Equal(2, await db.CommentVotes.CountAsync());

            // Aggregate recompute: post4001's rating_average/rating_count now reflect the three
            // migrated ratings against it (4, 5, 2), not the stale legacy denorm value (1.0/1)
            // this fixture seeded on purpose.
            var post4001Reloaded = await db.Posts.SingleAsync(p => p.LegacyId == 4001);
            Assert.Equal(3, post4001Reloaded.RatingCount);
            Assert.NotNull(post4001Reloaded.RatingAverage);
            Assert.True(Math.Abs(post4001Reloaded.RatingAverage!.Value - (11.0 / 3.0)) < 0.001);
        }

        var stats2 = await RunStepAsync();

        Assert.Equal(15, stats2.Read);
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("comment_deleted"));
        Assert.Equal(3, stats2.SkipReasons.GetValueOrDefault("comment_vaulted_unknown_type"));
        Assert.Equal(2, stats2.SkipReasons.GetValueOrDefault("comment_vaulted_target_deleted"));
        Assert.Equal(2, stats2.SkipReasons.GetValueOrDefault("comment_vaulted_target_missing"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("comment_content_empty"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("comment_rating_from_hidden_comment"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("comment_vote_duplicate"));
        Assert.Equal(2, stats2.SkipReasons.GetValueOrDefault("comment_vote_target_not_migrated"));
        // The vault is insert-only (ON CONFLICT DO NOTHING): none of its 7 rows count as inserted
        // or updated on a rerun. The 5 comments and 4 ratings already exist, so they count as
        // updated; the 2 votes already exist too, so they add nothing at all.
        Assert.Equal(0, stats2.Inserted);
        Assert.Equal(9, stats2.Updated);
        // The placeholder profile already exists on the second run, so it is not created again.
        Assert.DoesNotContain(stats2.Warnings, w => w.Contains("legacy user 402"));

        await using (var db = fixture.CreateTargetContext())
        {
            // Byte-stable: the vault, comments, ratings and votes this class owns are unchanged.
            Assert.Equal(7, await db.LegacyComments.CountAsync());
            Assert.Equal(5, await db.Comments.CountAsync(c => c.LegacyId >= 5000 && c.LegacyId < 6000));
            Assert.Equal(4, await db.Ratings.CountAsync(r => r.LegacyCommentId >= 5000 && r.LegacyCommentId < 6000));
            Assert.Equal(2, await db.CommentVotes.CountAsync());

            var comment5001 = await db.Comments.SingleAsync(c => c.LegacyId == 5001);
            Assert.Equal(2, comment5001.Score);

            var post4001 = await db.Posts.SingleAsync(p => p.LegacyId == 4001);
            Assert.Equal(3, post4001.RatingCount);
        }
    }

    // Owner legacy ids 400-402 and target legacy ids 4001 (post)/4101 (tank) are their own range,
    // disjoint from every other step test's fixtures. Seeded directly (idempotent find-or-create),
    // the same way PictureMigrationStepTests/TankMigrationStepTests seed their own prerequisites,
    // instead of running ProfileMigrationStep/TankMigrationStep/PictureMigrationStep.
    private Task RunPrerequisiteStepsAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var db = fixture.CreateTargetContext();

        if (!await db.Profiles.AnyAsync(p => p.LegacyId == 400))
        {
            db.Profiles.Add(new Profile
            {
                LegacyId = 400,
                Username = "comment-fixture-owner-400",
                DisplayName = "Comment Fixture Owner 400",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        if (!await db.Profiles.AnyAsync(p => p.LegacyId == 401))
        {
            db.Profiles.Add(new Profile
            {
                LegacyId = 401,
                Username = "comment-fixture-owner-401",
                DisplayName = "Comment Fixture Owner 401",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        await db.SaveChangesAsync();

        var owner400 = await db.Profiles.SingleAsync(p => p.LegacyId == 400);

        if (!await db.Posts.AnyAsync(p => p.LegacyId == 4001))
        {
            db.Posts.Add(new Post
            {
                LegacyId = 4001,
                AuthorProfileId = owner400.Id,
                Kind = PostKind.Single,
                Topic = PostTopic.Cichlids,
                State = PostState.Published,
                // A deliberately stale legacy-denorm average/count, so the recompute deviation
                // counter this step reports has something real to find.
                RatingAverage = 1.0,
                RatingCount = 1,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        if (!await db.Tanks.AnyAsync(t => t.LegacyId == 4101))
        {
            db.Tanks.Add(new Tank
            {
                LegacyId = 4101,
                ProfileId = owner400.Id,
                Title = "Comment Fixture Tank",
                State = TankState.Published,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        await db.SaveChangesAsync();
    });

    private Task<StepStatistics> RunStepAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, dryRun: false, CancellationToken.None);
        await EtlRunner.RunAsync(new CommentMigrationStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("comments");
    });
}
