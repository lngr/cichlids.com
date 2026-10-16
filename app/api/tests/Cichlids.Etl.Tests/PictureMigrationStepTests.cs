using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(EtlCollection.Name)]
public sealed class PictureMigrationStepTests(EtlFixture fixture)
{
    [Fact]
    public async Task MigratesPicturesIntoMediaItemsAndStoriesIdempotently()
    {
        await RunPrerequisiteStepsAsync();

        var stats1 = await RunStepAsync();
        string generatedSlug2011;

        // This step scans user_cichlids_pictures unconditionally (unlike TankMigrationStep, it has
        // no SQL-level deleted filter), so it also reads the two pre-existing fixture rows uid 1
        // and 2 that ProfileMigrationStepTests' own fixture relies on for its content-eligibility
        // scan: both have no pid recorded (defaults to 0), so they land in the generic
        // media-item-only, unmapped-pid path alongside this class's own uid 2007.
        // uid 2005 is soft-deleted; the other 16 (2001-2004 and 2006-2017) plus uid 1/2 are
        // active.
        Assert.Equal(19, stats1.Read);
        Assert.Equal(18, stats1.Inserted);
        Assert.Equal(0, stats1.Updated);
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("picture_deleted"));
        Assert.Equal(2, stats1.SkipReasons.GetValueOrDefault("picture_unmapped_pid_0"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("picture_unmapped_pid_9999"));
        // uid 1 and 2 both carry a NULL legacy image path (only uid/fe_user were ever seeded for
        // them), so they collide with each other on the same empty "unresolved" storage key too,
        // on top of this class's own uid 2013/2014 collision.
        Assert.Equal(2, stats1.SkipReasons.GetValueOrDefault("media_storage_key_collision"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("picture_alias_value_collision"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("tank_media_reference_missing"));
        // uid 2004 is the only active picture without an owner: one summary line, no line per row.
        Assert.Equal(
            "anonymous pictures (fe_user 0): 1 attributed to the community archive profile.",
            Assert.Single(stats1.Warnings, w => w.StartsWith("anonymous pictures", StringComparison.Ordinal)));
        Assert.DoesNotContain(stats1.Warnings, w => w.Contains("picture owner fe_user"));

        await using (var db = fixture.CreateTargetContext())
        {
            var owner301 = await db.Profiles.SingleAsync(p => p.LegacyId == 301);

            // pid 21: a published story with view/rating counters and its legacy alias.
            var media2001 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2001);
            Assert.Equal(MediaKind.Photo, media2001.Kind);
            Assert.Equal("originals/user_pics/301/pic1.jpg", media2001.StorageKey);
            Assert.Equal("pic1.jpg", media2001.OriginalFilename);
            Assert.Equal(owner301.Id, media2001.OwnerProfileId);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2000000050), media2001.CreatedAt);

            var story2001 = await db.Stories.SingleAsync(p => p.LegacyId == 2001);
            Assert.Equal(StoryTopic.Cichlids, story2001.Topic);
            Assert.Equal(StoryState.Published, story2001.State);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2000000100), story2001.PublishedAt);
            Assert.Equal(owner301.Id, story2001.AuthorProfileId);
            Assert.Equal("Pic One", story2001.Title);
            Assert.Equal("Desc one", story2001.Description);
            Assert.Equal(10, story2001.ViewCount);
            Assert.Equal(3, story2001.RatingCount);
            Assert.Equal(4.5, story2001.RatingAverage);

            var storyMedia2001 = await db.MediaLinks.SingleAsync(l => l.StoryId == story2001.Id);
            Assert.Equal(media2001.Id, storyMedia2001.MediaItemId);
            Assert.Equal(MediaLinkContext.Story, storyMedia2001.ContextType);
            Assert.Equal(MediaLinkRole.Item, storyMedia2001.Role);
            Assert.Equal(0, storyMedia2001.Position);
            Assert.Equal(media2001.CreatedAt, media2001.ReceivedAt);

            var alias2001 = await db.SlugAliases.SingleAsync(a => a.StoryId == story2001.Id);
            Assert.Equal("pic-one", alias2001.Value);
            Assert.True(alias2001.IsCanonical);

            // pid 29, hidden: a draft story with no legacy alias, so it gets a generated slug.
            var story2002 = await db.Stories.SingleAsync(p => p.LegacyId == 2002);
            Assert.Equal(StoryTopic.Tanks, story2002.Topic);
            Assert.Equal(StoryState.Draft, story2002.State);
            Assert.Null(story2002.PublishedAt);
            Assert.Null(story2002.RatingAverage);
            var alias2002 = await db.SlugAliases.SingleAsync(a => a.StoryId == story2002.Id);
            Assert.True(alias2002.IsCanonical);
            Assert.NotEqual("pic-one", alias2002.Value);
            // A generated slug is a fixed-length keyed hash, not the freeform text a legacy alias
            // carries over.
            Assert.Matches("^[0-9a-z]{8}$", alias2002.Value);

            // pid 21, owner has no migrated profile: a placeholder profile is created and the
            // story is archived regardless of the (unset) hidden flag.
            var placeholder950 = await db.Profiles.SingleAsync(p => p.LegacyId == 950);
            Assert.Equal(ProfileKind.Archived, placeholder950.Kind);
            var story2003 = await db.Stories.SingleAsync(p => p.LegacyId == 2003);
            Assert.Equal(placeholder950.Id, story2003.AuthorProfileId);
            Assert.Equal(StoryState.Archived, story2003.State);
            Assert.Null(story2003.PublishedAt);

            // pid 21, fe_user 0: attributed to the community archive, also archived.
            var communityArchive = await db.Profiles.SingleAsync(p => p.Username == "community-archive");
            var story2004 = await db.Stories.SingleAsync(p => p.LegacyId == 2004);
            Assert.Equal(communityArchive.Id, story2004.AuthorProfileId);
            Assert.Equal(StoryState.Archived, story2004.State);

            // uid 2005 (deleted) must not exist anywhere in the target.
            Assert.False(await db.MediaItems.AnyAsync(m => m.LegacyId == 2005));
            Assert.False(await db.Stories.AnyAsync(p => p.LegacyId == 2005));

            // pid 62: a video-extension, media-item-only picture (no story).
            var media2006 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2006);
            Assert.Equal(MediaKind.Video, media2006.Kind);
            Assert.False(await db.Stories.AnyAsync(p => p.LegacyId == 2006));

            // pid 9999: media-item-only, counted by its own pid.
            Assert.True(await db.MediaItems.AnyAsync(m => m.LegacyId == 2007));
            Assert.False(await db.Stories.AnyAsync(p => p.LegacyId == 2007));

            // pid 138/139: profile image and avatar backfill onto legacy fe_users uid 300.
            var profile300 = await db.Profiles.SingleAsync(p => p.LegacyId == 300);
            var media2008 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2008);
            var media2009 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2009);
            var profileLinks = await db.MediaLinks.Where(l => l.ProfileId == profile300.Id).ToListAsync();
            Assert.Equal(media2008.Id, Assert.Single(profileLinks, l => l.Role == MediaLinkRole.Header).MediaItemId);
            Assert.Equal(media2009.Id, Assert.Single(profileLinks, l => l.Role == MediaLinkRole.Avatar).MediaItemId);

            // uid 2010/2011 share the same realurl alias text; 2010 (the lower uniqalias uid)
            // keeps it, 2011 loses it and falls back to a generated slug.
            var story2010 = await db.Stories.SingleAsync(p => p.LegacyId == 2010);
            var alias2010 = await db.SlugAliases.SingleAsync(a => a.StoryId == story2010.Id);
            Assert.Equal("shared-slug", alias2010.Value);
            Assert.True(alias2010.IsCanonical);

            var story2011 = await db.Stories.SingleAsync(p => p.LegacyId == 2011);
            var alias2011 = await db.SlugAliases.SingleAsync(a => a.StoryId == story2011.Id);
            Assert.NotEqual("shared-slug", alias2011.Value);
            Assert.True(alias2011.IsCanonical);
            Assert.Matches("^[0-9a-z]{8}$", alias2011.Value);
            generatedSlug2011 = alias2011.Value;

            // uid 2012 has a hashid-looking alias and a normal-looking one; the normal one wins.
            var story2012 = await db.Stories.SingleAsync(p => p.LegacyId == 2012);
            var aliases2012 = await db.SlugAliases.Where(a => a.StoryId == story2012.Id).ToListAsync();
            Assert.Equal(2, aliases2012.Count);
            var canonical2012 = aliases2012.Single(a => a.IsCanonical);
            Assert.Equal("nice-slug-name", canonical2012.Value);
            Assert.Contains(aliases2012, a => a.Value == "ab12z" && !a.IsCanonical);

            // uid 2013/2014 share the same legacy image path; 2014 (the higher legacy id) is
            // suffixed to stay unique against 2013's plain key.
            var media2013 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2013);
            var media2014 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2014);
            Assert.Equal("originals/user_pics/dup/same.jpg", media2013.StorageKey);
            Assert.Equal("originals/user_pics/dup/same-2014.jpg", media2014.StorageKey);

            // uid 2015: a percent-encoded space and an umlaut in the legacy path, proving
            // StorageKeyNormalizer is actually wired into the step end to end (it also has its
            // own isolated unit tests covering the normalization rules themselves).
            var media2015 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2015);
            Assert.Equal("originals/user_pics/301/Gro_e_Gruppe.jpg", media2015.StorageKey);

            // uid 2016: no directory component at all, routed under the unresolved bucket.
            var media2016 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2016);
            Assert.Equal("originals/user_pics/unresolved/01_no_directory.jpg", media2016.StorageKey);

            // uid 2017: seeded with a nonzero legacy rating, which the INSERT path carries over.
            var story2017 = await db.Stories.SingleAsync(p => p.LegacyId == 2017);
            Assert.Equal(2.5, story2017.RatingAverage);
            Assert.Equal(4, story2017.RatingCount);

            // Tank media wiring: main image resolves, showcase carries the resolvable entry and
            // skips the dangling uid 2099, decoration carries the placeholder-owned picture.
            var media2003 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2003);
            var tank300 = await db.Tanks
                .Include(t => t.MediaLinks)
                .SingleAsync(t => t.LegacyId == 300);
            Assert.Equal(media2001.Id, Assert.Single(tank300.MediaLinks, l => l.Role == MediaLinkRole.Main).MediaItemId);
            Assert.Single(tank300.MediaLinks, l => l.Role == MediaLinkRole.Showcase && l.MediaItemId == media2006.Id);
            Assert.Single(tank300.MediaLinks, l => l.Role == MediaLinkRole.Decoration && l.MediaItemId == media2003.Id);
            Assert.DoesNotContain(tank300.MediaLinks, l => l.Role == MediaLinkRole.Technic);
        }

        var stats2 = await RunStepAsync();

        Assert.Equal(19, stats2.Read);
        Assert.Equal(0, stats2.Inserted);
        Assert.Equal(18, stats2.Updated);
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("picture_deleted"));
        Assert.Equal(2, stats2.SkipReasons.GetValueOrDefault("picture_unmapped_pid_0"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("picture_unmapped_pid_9999"));
        Assert.Equal(2, stats2.SkipReasons.GetValueOrDefault("media_storage_key_collision"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("picture_alias_value_collision"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("tank_media_reference_missing"));

        await using (var db = fixture.CreateTargetContext())
        {
            Assert.Equal(16, await db.MediaItems.CountAsync(m => m.LegacyId >= 2000 && m.LegacyId < 3000));
            Assert.Equal(12, await db.Stories.CountAsync(p => p.LegacyId >= 2000 && p.LegacyId < 3000));
            // Every story gets exactly one media link (all 12 stories from this class's fixture).
            Assert.Equal(12, await db.MediaLinks.CountAsync(l => l.StoryId != null));
            var tank300 = await db.Tanks.Include(t => t.MediaLinks).SingleAsync(t => t.LegacyId == 300);
            Assert.Equal(3, tank300.MediaLinks.Count);

            var media2013Second = await db.MediaItems.SingleAsync(m => m.LegacyId == 2013);
            var media2014Second = await db.MediaItems.SingleAsync(m => m.LegacyId == 2014);
            Assert.Equal("originals/user_pics/dup/same.jpg", media2013Second.StorageKey);
            Assert.Equal("originals/user_pics/dup/same-2014.jpg", media2014Second.StorageKey);

            var story2011Second = await db.Stories.SingleAsync(p => p.LegacyId == 2011);
            var alias2011Second = await db.SlugAliases.SingleAsync(a => a.StoryId == story2011Second.Id);
            // The generated slug must survive a re-run byte for byte: the story's own slug from
            // the previous run counts as available for it, not as a collision to retry past.
            Assert.Equal(generatedSlug2011, alias2011Second.Value);

            // A rerun must never re-seed the rating aggregate from the legacy row either: it stays
            // exactly what the first INSERT produced.
            var story2017Second = await db.Stories.SingleAsync(p => p.LegacyId == 2017);
            Assert.Equal(2.5, story2017Second.RatingAverage);
            Assert.Equal(4, story2017Second.RatingCount);
        }

        // uid 2017's story now stands in for what CommentMigrationStep does later in the pipeline:
        // it recomputes rating_average/rating_count from the rating table and expects them to stay
        // exactly as it left them. Simulate that here and rerun the pictures step once more: before
        // the PgBatchUpsert fix, this DO UPDATE clobbered both columns back to the legacy picture's
        // own denormalized rating/rating_count on every subsequent run.
        await fixture.RunExclusiveAsync(async () =>
        {
            await using var db = fixture.CreateTargetContext();
            var story2017 = await db.Stories.SingleAsync(p => p.LegacyId == 2017);
            story2017.RatingAverage = 1.25;
            story2017.RatingCount = 999;
            await db.SaveChangesAsync();
        });

        await RunStepAsync();

        await using (var db = fixture.CreateTargetContext())
        {
            var story2017Third = await db.Stories.SingleAsync(p => p.LegacyId == 2017);
            Assert.Equal(1.25, story2017Third.RatingAverage);
            Assert.Equal(999, story2017Third.RatingCount);
        }
    }

    // Owner legacy ids 300/301 and tank legacy id 300 are a range of their own, disjoint from the
    // species/profile (1-20) and tank/profile (10-14/101/102/999) fixtures other step tests own.
    // Seeded directly (idempotent find-or-create) the same way TankMigrationStepTests seeds its
    // own prerequisite rows, instead of running ProfileMigrationStep/TankMigrationStep.
    private Task RunPrerequisiteStepsAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var db = fixture.CreateTargetContext();

        if (!await db.Profiles.AnyAsync(p => p.LegacyId == 300))
        {
            db.Profiles.Add(new Profile
            {
                LegacyId = 300,
                Username = "picture-fixture-owner-300",
                DisplayName = "Picture Fixture Owner 300",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(2000000000),
            });
        }

        if (!await db.Profiles.AnyAsync(p => p.LegacyId == 301))
        {
            db.Profiles.Add(new Profile
            {
                LegacyId = 301,
                Username = "picture-fixture-owner-301",
                DisplayName = "Picture Fixture Owner 301",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(2000000000),
            });
        }

        await db.SaveChangesAsync();

        if (!await db.Tanks.AnyAsync(t => t.LegacyId == 300))
        {
            var owner301 = await db.Profiles.SingleAsync(p => p.LegacyId == 301);
            db.Tanks.Add(new Tank
            {
                LegacyId = 300,
                ProfileId = owner301.Id,
                Title = "Picture Fixture Tank",
                State = TankState.Published,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(2000000000),
            });
        }

        await db.SaveChangesAsync();
    });

    private Task<StepStatistics> RunStepAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, CancellationToken.None);
        await EtlRunner.RunAsync(new PictureMigrationStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("pictures");
    });
}
