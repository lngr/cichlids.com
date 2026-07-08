using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(EtlCollection.Name)]
public sealed class GalleryMigrationStepTests(EtlFixture fixture)
{
    [Fact]
    public async Task MigratesGalleriesToCollectionsIdempotently()
    {
        await RunPrerequisiteStepsAsync();

        var stats1 = await RunStepAsync();

        // uid 6004 (no mm rows) and uid 6005 (deleted) never pass the source query's filters, so
        // only uid 6001-6003 are read.
        Assert.Equal(3, stats1.Read);
        Assert.Equal(3, stats1.Inserted);
        Assert.Equal(0, stats1.Updated);
        Assert.Equal(2, stats1.SkipReasons.GetValueOrDefault("gallery_entry_picture_not_migrated"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("gallery_empty_after_filter"));

        await using (var db = fixture.CreateTargetContext())
        {
            var owner = await db.Profiles.SingleAsync(p => p.LegacyId == 450);
            var post4501 = await db.Posts.SingleAsync(p => p.LegacyId == 4501);
            var post4502 = await db.Posts.SingleAsync(p => p.LegacyId == 4502);

            // uid 6001: both pictures resolve, in mm sorting order.
            var collection6001 = await db.Collections
                .Include(c => c.Entries)
                .SingleAsync(c => c.LegacyId == 6001);
            Assert.Equal(owner.Id, collection6001.ProfileId);
            Assert.Equal("My Photos", collection6001.Title);
            Assert.True(collection6001.IsPublic);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), collection6001.CreatedAt);
            Assert.Equal(2, collection6001.Entries.Count);
            var first = collection6001.Entries.Single(e => e.Sort == 0);
            Assert.Equal(post4501.Id, first.PostId);
            var second = collection6001.Entries.Single(e => e.Sort == 1);
            Assert.Equal(post4502.Id, second.PostId);

            // uid 6002: hidden, blank title, no tstamp; one of its two pictures never migrated.
            var collection6002 = await db.Collections
                .Include(c => c.Entries)
                .SingleAsync(c => c.LegacyId == 6002);
            Assert.Equal("Gallery 6002", collection6002.Title);
            Assert.False(collection6002.IsPublic);
            Assert.Equal(new DateTimeOffset(2003, 1, 1, 0, 0, 0, TimeSpan.Zero), collection6002.CreatedAt);
            Assert.Single(collection6002.Entries);
            Assert.Equal(post4501.Id, collection6002.Entries[0].PostId);
            Assert.Equal(0, collection6002.Entries[0].Sort);

            // uid 6003: every picture failed to migrate -- the collection still exists, empty.
            var collection6003 = await db.Collections
                .Include(c => c.Entries)
                .SingleAsync(c => c.LegacyId == 6003);
            Assert.Empty(collection6003.Entries);

            // uid 6004/6005 never migrate at all.
            Assert.False(await db.Collections.AnyAsync(c => c.LegacyId == 6004));
            Assert.False(await db.Collections.AnyAsync(c => c.LegacyId == 6005));
        }

        var stats2 = await RunStepAsync();

        Assert.Equal(3, stats2.Read);
        Assert.Equal(0, stats2.Inserted);
        Assert.Equal(3, stats2.Updated);
        Assert.Equal(2, stats2.SkipReasons.GetValueOrDefault("gallery_entry_picture_not_migrated"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("gallery_empty_after_filter"));

        await using (var db = fixture.CreateTargetContext())
        {
            Assert.Equal(3, await db.Collections.CountAsync(c => c.LegacyId >= 6000 && c.LegacyId < 7000));

            var collection6001 = await db.Collections
                .Include(c => c.Entries)
                .SingleAsync(c => c.LegacyId == 6001);
            Assert.Equal(2, collection6001.Entries.Count);

            var collection6003 = await db.Collections
                .Include(c => c.Entries)
                .SingleAsync(c => c.LegacyId == 6003);
            Assert.Empty(collection6003.Entries);
        }
    }

    // Owner legacy id 450 and post legacy ids 4501/4502 are their own range, disjoint from every
    // other step test's fixtures (including CommentMigrationStepTests' 400s/4000s). Seeded
    // directly (idempotent find-or-create), the same way the other step tests seed their own
    // prerequisites, instead of running ProfileMigrationStep/PictureMigrationStep.
    private Task RunPrerequisiteStepsAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var db = fixture.CreateTargetContext();

        if (!await db.Profiles.AnyAsync(p => p.LegacyId == 450))
        {
            db.Profiles.Add(new Profile
            {
                LegacyId = 450,
                Username = "gallery-fixture-owner-450",
                DisplayName = "Gallery Fixture Owner 450",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        await db.SaveChangesAsync();

        var owner = await db.Profiles.SingleAsync(p => p.LegacyId == 450);

        if (!await db.Posts.AnyAsync(p => p.LegacyId == 4501))
        {
            db.Posts.Add(new Post
            {
                LegacyId = 4501,
                AuthorProfileId = owner.Id,
                Kind = PostKind.Single,
                Topic = PostTopic.Cichlids,
                State = PostState.Published,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        if (!await db.Posts.AnyAsync(p => p.LegacyId == 4502))
        {
            db.Posts.Add(new Post
            {
                LegacyId = 4502,
                AuthorProfileId = owner.Id,
                Kind = PostKind.Single,
                Topic = PostTopic.Cichlids,
                State = PostState.Published,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        await db.SaveChangesAsync();
    });

    private Task<StepStatistics> RunStepAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, dryRun: false, CancellationToken.None);
        await EtlRunner.RunAsync(new GalleryMigrationStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("galleries");
    });
}
