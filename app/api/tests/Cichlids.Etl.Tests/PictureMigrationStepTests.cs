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
    public async Task MigratesPicturesIntoMediaItemsAndPostsIdempotently()
    {
        await RunPrerequisiteStepsAsync();

        var stats1 = await RunStepAsync();

        // This step scans user_cichlids_pictures unconditionally (unlike TankMigrationStep, it has
        // no SQL-level deleted filter), so it also reads the two pre-existing fixture rows uid 1
        // and 2 that ProfileMigrationStepTests' own fixture relies on for its content-eligibility
        // scan: both have no pid recorded (defaults to 0), so they land in the generic
        // media-item-only, unmapped-pid path alongside this class's own uid 2007.
        // uid 2005 is soft-deleted; the other 13 (2001-2004, 2006-2014) plus uid 1/2 are active.
        Assert.Equal(16, stats1.Read);
        Assert.Equal(15, stats1.Inserted);
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

        await using (var db = fixture.CreateTargetContext())
        {
            var owner301 = await db.Profiles.SingleAsync(p => p.LegacyId == 301);

            // pid 21: a published post with view/rating counters and its legacy alias.
            var media2001 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2001);
            Assert.Equal(MediaKind.Photo, media2001.Kind);
            Assert.Equal("originals/user_pics/301/pic1.jpg", media2001.StorageKey);
            Assert.Equal("pic1.jpg", media2001.OriginalFilename);
            Assert.Equal(owner301.Id, media2001.OwnerProfileId);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2000000050), media2001.CreatedAt);

            var post2001 = await db.Posts.SingleAsync(p => p.LegacyId == 2001);
            Assert.Equal(PostTopic.Cichlids, post2001.Topic);
            Assert.Equal(PostState.Published, post2001.State);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2000000100), post2001.PublishedAt);
            Assert.Equal(owner301.Id, post2001.AuthorProfileId);
            Assert.Equal("Pic One", post2001.Title);
            Assert.Equal("Desc one", post2001.Description);
            Assert.Equal(10, post2001.ViewCount);
            Assert.Equal(3, post2001.RatingCount);
            Assert.Equal(4.5, post2001.RatingAverage);

            var postMedia2001 = await db.PostMedia.SingleAsync(pm => pm.PostId == post2001.Id);
            Assert.Equal(media2001.Id, postMedia2001.MediaItemId);
            Assert.Equal(0, postMedia2001.Sort);

            var alias2001 = await db.SlugAliases.SingleAsync(a => a.PostId == post2001.Id);
            Assert.Equal("pic-one", alias2001.Value);
            Assert.True(alias2001.IsCanonical);

            // pid 29, hidden: a draft post with no legacy alias, so it gets a generated slug.
            var post2002 = await db.Posts.SingleAsync(p => p.LegacyId == 2002);
            Assert.Equal(PostTopic.Tanks, post2002.Topic);
            Assert.Equal(PostState.Draft, post2002.State);
            Assert.Null(post2002.PublishedAt);
            Assert.Null(post2002.RatingAverage);
            var alias2002 = await db.SlugAliases.SingleAsync(a => a.PostId == post2002.Id);
            Assert.True(alias2002.IsCanonical);
            Assert.NotEqual("pic-one", alias2002.Value);

            // pid 21, owner has no migrated profile: a placeholder profile is created and the
            // post is archived regardless of the (unset) hidden flag.
            var placeholder950 = await db.Profiles.SingleAsync(p => p.LegacyId == 950);
            Assert.Equal(ProfileKind.Archived, placeholder950.Kind);
            var post2003 = await db.Posts.SingleAsync(p => p.LegacyId == 2003);
            Assert.Equal(placeholder950.Id, post2003.AuthorProfileId);
            Assert.Equal(PostState.Archived, post2003.State);
            Assert.Null(post2003.PublishedAt);

            // pid 21, fe_user 0: attributed to the community archive, also archived.
            var communityArchive = await db.Profiles.SingleAsync(p => p.Username == "community-archive");
            var post2004 = await db.Posts.SingleAsync(p => p.LegacyId == 2004);
            Assert.Equal(communityArchive.Id, post2004.AuthorProfileId);
            Assert.Equal(PostState.Archived, post2004.State);

            // uid 2005 (deleted) must not exist anywhere in the target.
            Assert.False(await db.MediaItems.AnyAsync(m => m.LegacyId == 2005));
            Assert.False(await db.Posts.AnyAsync(p => p.LegacyId == 2005));

            // pid 62: a video-extension, media-item-only picture (no post).
            var media2006 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2006);
            Assert.Equal(MediaKind.Video, media2006.Kind);
            Assert.False(await db.Posts.AnyAsync(p => p.LegacyId == 2006));

            // pid 9999: media-item-only, counted by its own pid.
            Assert.True(await db.MediaItems.AnyAsync(m => m.LegacyId == 2007));
            Assert.False(await db.Posts.AnyAsync(p => p.LegacyId == 2007));

            // pid 138/139: profile image and avatar backfill onto legacy fe_users uid 300.
            var profile300 = await db.Profiles.SingleAsync(p => p.LegacyId == 300);
            var media2008 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2008);
            var media2009 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2009);
            Assert.Equal(media2008.Id, profile300.ProfileImageMediaId);
            Assert.Equal(media2009.Id, profile300.AvatarMediaId);

            // uid 2010/2011 share the same realurl alias text; 2010 (the lower uniqalias uid)
            // keeps it, 2011 loses it and falls back to a generated slug.
            var post2010 = await db.Posts.SingleAsync(p => p.LegacyId == 2010);
            var alias2010 = await db.SlugAliases.SingleAsync(a => a.PostId == post2010.Id);
            Assert.Equal("shared-slug", alias2010.Value);
            Assert.True(alias2010.IsCanonical);

            var post2011 = await db.Posts.SingleAsync(p => p.LegacyId == 2011);
            var alias2011 = await db.SlugAliases.SingleAsync(a => a.PostId == post2011.Id);
            Assert.NotEqual("shared-slug", alias2011.Value);
            Assert.True(alias2011.IsCanonical);

            // uid 2012 has a hashid-looking alias and a normal-looking one; the normal one wins.
            var post2012 = await db.Posts.SingleAsync(p => p.LegacyId == 2012);
            var aliases2012 = await db.SlugAliases.Where(a => a.PostId == post2012.Id).ToListAsync();
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

            // Tank media wiring: main image resolves, showcase carries the resolvable entry and
            // skips the dangling uid 2099, decoration carries the placeholder-owned picture.
            var media2003 = await db.MediaItems.SingleAsync(m => m.LegacyId == 2003);
            var tank300 = await db.Tanks
                .Include(t => t.Media)
                .SingleAsync(t => t.LegacyId == 300);
            Assert.Equal(media2001.Id, tank300.MainMediaId);
            Assert.Single(tank300.Media, m => m.Section == TankMediaSection.Showcase && m.MediaItemId == media2006.Id);
            Assert.Single(tank300.Media, m => m.Section == TankMediaSection.Decoration && m.MediaItemId == media2003.Id);
            Assert.DoesNotContain(tank300.Media, m => m.Section == TankMediaSection.Technic);
        }

        var stats2 = await RunStepAsync();

        Assert.Equal(16, stats2.Read);
        Assert.Equal(0, stats2.Inserted);
        Assert.Equal(15, stats2.Updated);
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("picture_deleted"));
        Assert.Equal(2, stats2.SkipReasons.GetValueOrDefault("picture_unmapped_pid_0"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("picture_unmapped_pid_9999"));
        Assert.Equal(2, stats2.SkipReasons.GetValueOrDefault("media_storage_key_collision"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("picture_alias_value_collision"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("tank_media_reference_missing"));

        await using (var db = fixture.CreateTargetContext())
        {
            Assert.Equal(13, await db.MediaItems.CountAsync(m => m.LegacyId >= 2000 && m.LegacyId < 3000));
            Assert.Equal(9, await db.Posts.CountAsync(p => p.LegacyId >= 2000 && p.LegacyId < 3000));
            // Every post gets exactly one post_media row (all 9 posts from this class's fixture).
            Assert.Equal(9, await db.PostMedia.CountAsync());
            var tank300 = await db.Tanks.Include(t => t.Media).SingleAsync(t => t.LegacyId == 300);
            Assert.Equal(2, tank300.Media.Count);

            var media2013Second = await db.MediaItems.SingleAsync(m => m.LegacyId == 2013);
            var media2014Second = await db.MediaItems.SingleAsync(m => m.LegacyId == 2014);
            Assert.Equal("originals/user_pics/dup/same.jpg", media2013Second.StorageKey);
            Assert.Equal("originals/user_pics/dup/same-2014.jpg", media2014Second.StorageKey);

            var post2011Second = await db.Posts.SingleAsync(p => p.LegacyId == 2011);
            var alias2011Second = await db.SlugAliases.SingleAsync(a => a.PostId == post2011Second.Id);
            Assert.NotEqual("shared-slug", alias2011Second.Value);
        }
    }

    // Owner legacy ids 300/301 and tank legacy id 300 are a range of their own, disjoint from the
    // species/profile (1-16) and tank/profile (10-14/101/102/999) fixtures other step tests own.
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
            fixture.LegacyConnectionString, fixture.TargetConnectionString, dryRun: false, CancellationToken.None);
        await EtlRunner.RunAsync(new PictureMigrationStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("pictures");
    });
}
