using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Tests.Persistence;

/// <summary>
/// The visibility of a media item for a viewer is the union of the visibility of its links; the owner
/// always sees the own files and a file without links is visible to its owner only.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MediaVisibilityTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private sealed record World(CichlidsDbContext Db, Profile Owner, Profile Member)
    {
        public async Task<MediaItem> ItemAsync(Profile? owner = null)
        {
            var item = new MediaItem
            {
                OwnerProfileId = (owner ?? Owner).Id, Kind = MediaKind.Photo, StorageKey = $"originals/{Guid.NewGuid():N}.jpg", CreatedAt = Now, ReceivedAt = Now,
            };
            Db.MediaItems.Add(item);
            await Db.SaveChangesAsync();
            return item;
        }

        public async Task<Tank> TankAsync(TankVisibility profile, TankVisibility stream, TankState state = TankState.Published)
        {
            var tank = new Tank
            {
                ProfileId = Owner.Id, Title = "Tank", State = state, CreatedAt = Now, PublishedAt = Now, ProfileVisibility = profile, StreamVisibility = stream,
            };
            Db.Tanks.Add(tank);
            await Db.SaveChangesAsync();
            return tank;
        }

        public async Task LinkAsync(MediaLink link)
        {
            Db.MediaLinks.Add(link);
            await Db.SaveChangesAsync();
        }

        public async Task<CareEntry> EntryAsync(Tank tank, bool onlyMe = false, long? sessionId = null)
        {
            var entry = new CareEntry
            {
                TankId = tank.Id, AuthorProfileId = Owner.Id, Kind = CareKind.WaterChange, OccurredAt = Now, RecordedAt = Now, OnlyMe = onlyMe,
                WaterChangeSessionId = sessionId,
            };
            Db.CareEntries.Add(entry);
            await Db.SaveChangesAsync();
            return entry;
        }

        public async Task<Story> PostAsync(StoryState state, DateTimeOffset? deletedAt = null)
        {
            var story = new Story
            {
                AuthorProfileId = Owner.Id, Topic = StoryTopic.Cichlids, State = state, CreatedAt = Now, UpdatedAt = Now, DeletedAt = deletedAt,
            };
            Db.Stories.Add(story);
            await Db.SaveChangesAsync();
            return story;
        }

        public async Task<bool> SeesAsync(MediaItem item, Profile? viewer) =>
            (await Db.VisibleMediaItemIdsAsync([item.Id], viewer?.Id, Now)).Contains(item.Id);

        public async Task<bool> AnonymousSeesAsync(MediaItem item) =>
            (await Db.AnonymousVisibleMediaItemIdsAsync([item.Id], Now)).Contains(item.Id);
    }

    private async Task<World> NewWorldAsync()
    {
        var db = fixture.CreateContext();
        return new World(db, await TestDataBuilder.CreateProfileAsync(db), await TestDataBuilder.CreateProfileAsync(db));
    }

    [Fact]
    public async Task GivenAnUnlinkedFile_ThenOnlyItsOwnerSeesIt()
    {
        var w = await NewWorldAsync();
        var item = await w.ItemAsync();

        Assert.True(await w.SeesAsync(item, w.Owner));
        Assert.False(await w.SeesAsync(item, w.Member));
        Assert.False(await w.SeesAsync(item, null));
        Assert.False(await w.AnonymousSeesAsync(item));
    }

    [Theory]
    [InlineData(TankVisibility.Public, true)]
    [InlineData(TankVisibility.Private, false)]
    public async Task GivenATankLink_ThenTheProfileVisibilityOfTheTankDecides(TankVisibility profile, bool others)
    {
        var w = await NewWorldAsync();
        var tank = await w.TankAsync(profile, TankVisibility.Private);
        foreach (var role in new[] { MediaLinkRole.Showcase, MediaLinkRole.TimeLapse, MediaLinkRole.Main, MediaLinkRole.Video })
        {
            var item = await w.ItemAsync();
            await w.LinkAsync(MediaLink.ForTank(tank.Id, item.Id, role));
            Assert.True(await w.SeesAsync(item, w.Owner));
            Assert.Equal(others, await w.SeesAsync(item, w.Member));
            Assert.Equal(others, await w.AnonymousSeesAsync(item));
        }
    }

    [Fact]
    public async Task GivenADraftTank_ThenOnlyItsOwnerSeesItsPhotos()
    {
        var w = await NewWorldAsync();
        var tank = await w.TankAsync(TankVisibility.Public, TankVisibility.Public, TankState.Draft);
        var item = await w.ItemAsync();
        await w.LinkAsync(MediaLink.ForTank(tank.Id, item.Id, MediaLinkRole.Showcase));

        Assert.False(await w.AnonymousSeesAsync(item));
        Assert.False(await w.SeesAsync(item, w.Member));
    }

    [Theory]
    [InlineData(TankVisibility.Public, TankVisibility.Public, true)]
    [InlineData(TankVisibility.Public, TankVisibility.Private, false)]
    [InlineData(TankVisibility.Private, TankVisibility.Private, false)]
    public async Task GivenAProofPhoto_ThenTheHistoryVisibilityOfTheTankDecides(TankVisibility profile, TankVisibility stream, bool others)
    {
        var w = await NewWorldAsync();
        var tank = await w.TankAsync(profile, stream);
        foreach (var role in new[] { MediaLinkRole.Decommission, MediaLinkRole.Recommission })
        {
            var item = await w.ItemAsync();
            await w.LinkAsync(MediaLink.ForTank(tank.Id, item.Id, role));
            Assert.True(await w.SeesAsync(item, w.Owner));
            Assert.Equal(others, await w.SeesAsync(item, w.Member));
            Assert.Equal(others, await w.AnonymousSeesAsync(item));
        }
    }

    [Theory]
    [InlineData(TankVisibility.Public, TankVisibility.Public, false, true)]
    [InlineData(TankVisibility.Public, TankVisibility.Public, true, false)]
    [InlineData(TankVisibility.Public, TankVisibility.Private, false, false)]
    [InlineData(TankVisibility.Private, TankVisibility.Private, false, false)]
    public async Task GivenACareEntryPhoto_ThenTheHistoryVisibilityAndOnlyMeDecide(
        TankVisibility profile, TankVisibility stream, bool onlyMe, bool others)
    {
        var w = await NewWorldAsync();
        var tank = await w.TankAsync(profile, stream);
        var entry = await w.EntryAsync(tank, onlyMe);
        var item = await w.ItemAsync();
        await w.LinkAsync(MediaLink.ForCareEntry(entry.Id, item.Id, MediaLinkRole.Photo));

        Assert.True(await w.SeesAsync(item, w.Owner));
        Assert.Equal(others, await w.SeesAsync(item, w.Member));
        Assert.Equal(others, await w.AnonymousSeesAsync(item));
    }

    [Fact]
    public async Task GivenEvidenceOfAnOpenSession_ThenOnlyTheOwnerSeesIt_AndAfterFinishingTheEntryDecides()
    {
        var w = await NewWorldAsync();
        var tank = await w.TankAsync(TankVisibility.Public, TankVisibility.Public);
        var session = new WaterChangeSession { TankId = tank.Id, ProfileId = w.Owner.Id, StartedAt = Now };
        w.Db.WaterChangeSessions.Add(session);
        await w.Db.SaveChangesAsync();
        var item = await w.ItemAsync();
        await w.LinkAsync(MediaLink.ForWaterChangeSession(session.Id, item.Id));

        Assert.True(await w.SeesAsync(item, w.Owner));
        Assert.False(await w.SeesAsync(item, w.Member));
        Assert.False(await w.AnonymousSeesAsync(item));

        var entry = await w.EntryAsync(tank, onlyMe: false, session.Id);
        Assert.True(await w.AnonymousSeesAsync(item));

        entry.OnlyMe = true;
        await w.Db.SaveChangesAsync();
        Assert.False(await w.AnonymousSeesAsync(item));
        Assert.False(await w.SeesAsync(item, w.Member));
    }

    [Theory]
    [InlineData(TankVisibility.Public, true)]
    [InlineData(TankVisibility.Private, false)]
    public async Task GivenAnInhabitantPhoto_ThenTheVisibilityOfItsTankDecides(TankVisibility profile, bool others)
    {
        // The role check of the link table lists no role of an inhabitant, so this database drops it.
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE public.media_link DROP CONSTRAINT ck_media_link_role");
        var w = new World(db, await TestDataBuilder.CreateProfileAsync(db), await TestDataBuilder.CreateProfileAsync(db));
        var tank = await w.TankAsync(profile, TankVisibility.Private);
        var inhabitant = new Inhabitant { TankId = tank.Id };
        db.Inhabitants.Add(inhabitant);
        await db.SaveChangesAsync();
        var item = await w.ItemAsync();
        await w.LinkAsync(new MediaLink
        {
            ContextType = MediaLinkContext.Inhabitant, InhabitantId = inhabitant.Id, MediaItemId = item.Id, Role = MediaLinkRole.Showcase,
        });

        Assert.True(await w.SeesAsync(item, w.Owner));
        Assert.Equal(others, await w.SeesAsync(item, w.Member));
        Assert.Equal(others, await w.AnonymousSeesAsync(item));
    }

    [Fact]
    public async Task GivenAPostLink_ThenPublishedIsPublicDraftIsTheAuthorsAndRemovedIsNobodys()
    {
        var w = await NewWorldAsync();
        var published = await w.PostAsync(StoryState.Published);
        var draft = await w.PostAsync(StoryState.Draft);
        var archived = await w.PostAsync(StoryState.Archived);
        var deleted = await w.PostAsync(StoryState.Published, Now);
        var uploader = await TestDataBuilder.CreateProfileAsync(w.Db);
        var items = new Dictionary<string, MediaItem>();
        foreach (var (name, story) in new[] { ("published", published), ("draft", draft), ("archived", archived), ("deleted", deleted) })
        {
            // The files belong to another member, so the owner clause does not mask the story rule.
            items[name] = await w.ItemAsync(uploader);
            await w.LinkAsync(MediaLink.ForStory(story.Id, items[name].Id));
        }

        Assert.True(await w.AnonymousSeesAsync(items["published"]));
        Assert.True(await w.SeesAsync(items["published"], w.Member));
        Assert.False(await w.AnonymousSeesAsync(items["draft"]));
        Assert.False(await w.SeesAsync(items["draft"], w.Member));
        Assert.True(await w.SeesAsync(items["draft"], w.Owner));
        foreach (var name in new[] { "archived", "deleted" })
        {
            Assert.False(await w.AnonymousSeesAsync(items[name]));
            Assert.False(await w.SeesAsync(items[name], w.Owner));
            Assert.False(await w.SeesAsync(items[name], w.Member));
            Assert.True(await w.SeesAsync(items[name], uploader));
        }
    }

    [Fact]
    public async Task GivenAPhotoOfAPrivateTankInAPublishedStory_ThenEveryoneSeesThePhotoAndTheTankStaysHidden()
    {
        var w = await NewWorldAsync();
        var tank = await w.TankAsync(TankVisibility.Private, TankVisibility.Private);
        var item = await w.ItemAsync();
        await w.LinkAsync(MediaLink.ForTank(tank.Id, item.Id, MediaLinkRole.Showcase));
        Assert.False(await w.AnonymousSeesAsync(item));

        var story = await w.PostAsync(StoryState.Published);
        await w.LinkAsync(MediaLink.ForStory(story.Id, item.Id));

        Assert.True(await w.AnonymousSeesAsync(item));
        Assert.True(await w.SeesAsync(item, w.Member));
    }

    [Fact]
    public async Task GivenADiscussionAttachment_ThenEveryoneSeesIt()
    {
        var w = await NewWorldAsync();
        var thread = new DiscussionThread { Title = "T", CreatedAt = Now, Category = DiscussionCategory.Cichlids };
        w.Db.DiscussionThreads.Add(thread);
        await w.Db.SaveChangesAsync();
        var story = new DiscussionPost { ThreadId = thread.Id, Body = "b", CreatedAt = Now };
        w.Db.DiscussionPosts.Add(story);
        await w.Db.SaveChangesAsync();
        var item = await w.ItemAsync();
        await w.LinkAsync(MediaLink.ForDiscussionPost(story.Id, item.Id));

        Assert.True(await w.AnonymousSeesAsync(item));
    }

    [Fact]
    public async Task GivenAProfilePicture_ThenAnActiveProfileShowsItAndASuspendedProfileDoesNot()
    {
        var w = await NewWorldAsync();
        var item = await w.ItemAsync();
        await w.LinkAsync(MediaLink.ForProfile(w.Owner.Id, item.Id, MediaLinkRole.Avatar));
        Assert.True(await w.AnonymousSeesAsync(item));

        w.Owner.SuspendedUntil = Now.AddDays(1);
        await w.Db.SaveChangesAsync();
        Assert.False(await w.AnonymousSeesAsync(item));
        Assert.True(await w.SeesAsync(item, w.Owner));
    }

    [Fact]
    public async Task GivenManyFiles_ThenOneQueryAnswersForAllOfThem()
    {
        var w = await NewWorldAsync();
        var publicTank = await w.TankAsync(TankVisibility.Public, TankVisibility.Public);
        var privateTank = await w.TankAsync(TankVisibility.Private, TankVisibility.Private);
        var visible = new List<long>();
        var hidden = new List<long>();
        for (var i = 0; i < 20; i++)
        {
            var open = await w.ItemAsync();
            await w.LinkAsync(MediaLink.ForTank(publicTank.Id, open.Id, MediaLinkRole.TimeLapse, i));
            visible.Add(open.Id);
            var closed = await w.ItemAsync();
            await w.LinkAsync(MediaLink.ForTank(privateTank.Id, closed.Id, MediaLinkRole.TimeLapse, i));
            hidden.Add(closed.Id);
        }

        var seen = await w.Db.VisibleMediaItemIdsAsync([.. visible, .. hidden], w.Member.Id, Now);

        Assert.Equal(visible.Order(), seen.Order());
    }

    [Fact]
    public async Task GivenAFollowersTank_ThenApprovedFollowersAndSelectedMembersSeeTheMediaAndAnonymousViewersDoNot()
    {
        var w = await NewWorldAsync();
        var follower = await TestDataBuilder.CreateProfileAsync(w.Db);
        var requester = await TestDataBuilder.CreateProfileAsync(w.Db);
        var selected = await TestDataBuilder.CreateProfileAsync(w.Db);
        var tank = await w.TankAsync(TankVisibility.Followers, TankVisibility.Selected);
        var tankItem = await w.ItemAsync();
        await w.LinkAsync(MediaLink.ForTank(tank.Id, tankItem.Id, MediaLinkRole.Showcase));
        var entryItem = await w.ItemAsync();
        var entry = await w.EntryAsync(tank);
        await w.LinkAsync(MediaLink.ForCareEntry(entry.Id, entryItem.Id, MediaLinkRole.Photo));
        w.Db.Follows.AddRange(
            new Follow { FollowerProfileId = follower.Id, TargetTankId = tank.Id, Status = FollowStatus.Approved, CreatedAt = Now },
            new Follow { FollowerProfileId = requester.Id, TargetTankId = tank.Id, Status = FollowStatus.Pending, CreatedAt = Now });
        w.Db.TankSelectedMembers.Add(new TankSelectedMember { TankId = tank.Id, MemberProfileId = selected.Id, AddedAt = Now });
        await w.Db.SaveChangesAsync();

        // The profile level followers reaches follower and selected member; the stream level selected only the selected member.
        Assert.True(await w.SeesAsync(tankItem, w.Owner));
        Assert.True(await w.SeesAsync(tankItem, follower));
        Assert.True(await w.SeesAsync(tankItem, selected));
        Assert.False(await w.SeesAsync(tankItem, requester));
        Assert.False(await w.SeesAsync(tankItem, w.Member));
        Assert.False(await w.SeesAsync(tankItem, null));
        Assert.False(await w.AnonymousSeesAsync(tankItem));
        Assert.True(await w.SeesAsync(entryItem, w.Owner));
        Assert.True(await w.SeesAsync(entryItem, selected));
        Assert.False(await w.SeesAsync(entryItem, follower));
        Assert.False(await w.SeesAsync(entryItem, requester));
        Assert.False(await w.AnonymousSeesAsync(entryItem));
    }

    [Fact]
    public async Task GivenASelectedTank_ThenOnlyTheSelectedMemberSeesTheMedia()
    {
        var w = await NewWorldAsync();
        var follower = await TestDataBuilder.CreateProfileAsync(w.Db);
        var tank = await w.TankAsync(TankVisibility.Selected, TankVisibility.Selected);
        var item = await w.ItemAsync();
        await w.LinkAsync(MediaLink.ForTank(tank.Id, item.Id, MediaLinkRole.Showcase));
        w.Db.Follows.Add(new Follow { FollowerProfileId = follower.Id, TargetTankId = tank.Id, Status = FollowStatus.Approved, CreatedAt = Now });
        w.Db.TankSelectedMembers.Add(new TankSelectedMember { TankId = tank.Id, MemberProfileId = w.Member.Id, AddedAt = Now });
        await w.Db.SaveChangesAsync();

        Assert.True(await w.SeesAsync(item, w.Member));
        Assert.False(await w.SeesAsync(item, follower));
        Assert.False(await w.AnonymousSeesAsync(item));
    }
}

