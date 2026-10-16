using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Tests.Persistence;

/// <summary>
/// Every write path that can alter who sees a media item changes its visibility generation and marks it
/// for placement in the transaction of the change, whichever statement makes the change.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MediaVisibilityGenerationTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private sealed record Setup(Profile Owner, Tank Tank, CareEntry Entry, WaterChangeSession Session, Story Story, long TankItem, long EntryItem, long SessionItem, long StoryItem);

    private async Task<Setup> NewSetupAsync()
    {
        await using var db = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(db);
        var tank = new Tank { ProfileId = owner.Id, Title = "T", State = TankState.Published, CreatedAt = Now, PublishedAt = Now };
        db.Tanks.Add(tank);
        await db.SaveChangesAsync();
        var entry = new CareEntry { TankId = tank.Id, AuthorProfileId = owner.Id, Kind = CareKind.WaterChange, OccurredAt = Now, RecordedAt = Now };
        var session = new WaterChangeSession { TankId = tank.Id, ProfileId = owner.Id, StartedAt = Now };
        var story = new Story { AuthorProfileId = owner.Id, Topic = StoryTopic.Cichlids, State = StoryState.Published, CreatedAt = Now, UpdatedAt = Now };
        db.CareEntries.Add(entry);
        db.WaterChangeSessions.Add(session);
        db.Stories.Add(story);
        await db.SaveChangesAsync();
        long Item()
        {
            var item = new MediaItem { OwnerProfileId = owner.Id, Kind = MediaKind.Photo, StorageKey = $"originals/{Guid.NewGuid():N}.jpg", CreatedAt = Now, ReceivedAt = Now };
            db.MediaItems.Add(item);
            db.SaveChanges();
            return item.Id;
        }

        var (tankItem, entryItem, sessionItem, storyItem) = (Item(), Item(), Item(), Item());
        db.MediaLinks.AddRange(
            MediaLink.ForTank(tank.Id, tankItem, MediaLinkRole.Showcase), MediaLink.ForCareEntry(entry.Id, entryItem, MediaLinkRole.Photo),
            MediaLink.ForWaterChangeSession(session.Id, sessionItem), MediaLink.ForStory(story.Id, storyItem));
        await db.SaveChangesAsync();
        await SettleAsync(tankItem, entryItem, sessionItem, storyItem);
        return new Setup(owner, tank, entry, session, story, tankItem, entryItem, sessionItem, storyItem);
    }

    private async Task SettleAsync(params long[] items)
    {
        await using var db = fixture.CreateContext();
        await db.MediaItems.Where(m => items.Contains(m.Id)).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.PlacementPending, false).SetProperty(m => m.PlacementAttempts, 2));
    }

    private async Task<(long Generation, bool Pending, int Attempts)> StateAsync(long item)
    {
        await using var db = fixture.CreateContext();
        var m = await db.MediaItems.AsNoTracking().SingleAsync(x => x.Id == item);
        return (m.VisibilityGeneration, m.PlacementPending, m.PlacementAttempts);
    }

    private async Task AssertChangedAsync(long item, Func<CichlidsDbContext, Task> change)
    {
        var before = await StateAsync(item);
        Assert.False(before.Pending);
        await using var db = fixture.CreateContext();
        await change(db);
        var after = await StateAsync(item);
        Assert.NotEqual(before.Generation, after.Generation);
        Assert.True(after.Pending);
        Assert.Equal(0, after.Attempts);
    }

    private async Task AssertUnchangedAsync(long item, Func<CichlidsDbContext, Task> change)
    {
        var before = await StateAsync(item);
        await using var db = fixture.CreateContext();
        await change(db);
        Assert.Equal(before, await StateAsync(item));
    }

    [Fact]
    public async Task GivenATank_WhenItsProfileVisibilityChanges_ThenAllItsItemsChange()
    {
        var s = await NewSetupAsync();
        await AssertChangedAsync(s.TankItem, db => db.Tanks.Where(t => t.Id == s.Tank.Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.ProfileVisibility, TankVisibility.Private).SetProperty(t => t.StreamVisibility, TankVisibility.Private)));
        foreach (var item in new[] { s.EntryItem, s.SessionItem })
        {
            Assert.True((await StateAsync(item)).Pending);
        }
    }

    [Fact]
    public async Task GivenATank_WhenItsStreamVisibilityChanges_ThenItsItemsChange()
    {
        var s = await NewSetupAsync();
        await AssertChangedAsync(s.EntryItem, db => db.Tanks.Where(t => t.Id == s.Tank.Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.StreamVisibility, TankVisibility.Private)));
    }

    [Fact]
    public async Task GivenATank_WhenItIsUnpublishedOrDeleted_ThenItsItemsChange()
    {
        var s = await NewSetupAsync();
        await AssertChangedAsync(s.TankItem, db => db.Tanks.Where(t => t.Id == s.Tank.Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.State, TankState.Draft)));
        await SettleAsync(s.TankItem);
        await AssertChangedAsync(s.TankItem, db => db.Tanks.Where(t => t.Id == s.Tank.Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.DeletedAt, Now)));
    }

    [Fact]
    public async Task GivenATank_WhenOnlyItsTitleChanges_ThenNothingChanges()
    {
        var s = await NewSetupAsync();
        await AssertUnchangedAsync(s.TankItem, db => db.Tanks.Where(t => t.Id == s.Tank.Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.Title, "Other")));
    }

    [Fact]
    public async Task GivenAnEntry_WhenItIsSetToOnlyMe_ThenItsPhotoChanges()
    {
        var s = await NewSetupAsync();
        await AssertChangedAsync(s.EntryItem, db => db.CareEntries.Where(e => e.Id == s.Entry.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.OnlyMe, true)));
    }

    [Fact]
    public async Task GivenAnEntry_WhenItsNoteChanges_ThenNothingChanges()
    {
        var s = await NewSetupAsync();
        await AssertUnchangedAsync(s.EntryItem, db => db.CareEntries.Where(e => e.Id == s.Entry.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.Note, "n")));
    }

    [Fact]
    public async Task GivenAnEntryThatFinishesASession_WhenItIsCreatedOrDeleted_ThenTheEvidenceChanges()
    {
        var s = await NewSetupAsync();
        long entryId = 0;
        await AssertChangedAsync(s.SessionItem, async db =>
        {
            var entry = new CareEntry { TankId = s.Tank.Id, AuthorProfileId = s.Owner.Id, Kind = CareKind.WaterChange, OccurredAt = Now, RecordedAt = Now, WaterChangeSessionId = s.Session.Id };
            db.CareEntries.Add(entry);
            await db.SaveChangesAsync();
            entryId = entry.Id;
        });
        await SettleAsync(s.SessionItem);
        await AssertChangedAsync(s.SessionItem, db => db.CareEntries.Where(e => e.Id == entryId).ExecuteDeleteAsync());
    }

    [Fact]
    public async Task GivenAPost_WhenItIsWithdrawnArchivedOrRemoved_ThenItsItemsChange()
    {
        var s = await NewSetupAsync();
        await AssertChangedAsync(s.StoryItem, db => db.Stories.Where(p => p.Id == s.Story.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.State, StoryState.Draft)));
        await SettleAsync(s.StoryItem);
        await AssertChangedAsync(s.StoryItem, db => db.Stories.Where(p => p.Id == s.Story.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.State, StoryState.Archived)));
        await SettleAsync(s.StoryItem);
        await AssertChangedAsync(s.StoryItem, db => db.Stories.Where(p => p.Id == s.Story.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.DeletedAt, Now)));
    }

    [Fact]
    public async Task GivenAProfilePicture_WhenTheProfileIsSuspended_ThenItsPictureChanges()
    {
        var s = await NewSetupAsync();
        long item;
        await using (var db = fixture.CreateContext())
        {
            var picture = new MediaItem { OwnerProfileId = s.Owner.Id, Kind = MediaKind.Photo, StorageKey = $"originals/{Guid.NewGuid():N}.jpg", CreatedAt = Now, ReceivedAt = Now };
            db.MediaItems.Add(picture);
            await db.SaveChangesAsync();
            db.MediaLinks.Add(MediaLink.ForProfile(s.Owner.Id, picture.Id, MediaLinkRole.Avatar));
            await db.SaveChangesAsync();
            item = picture.Id;
        }

        await SettleAsync(item);
        await AssertChangedAsync(item, db => db.Profiles.Where(p => p.Id == s.Owner.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.SuspendedUntil, Now.AddDays(1))));
    }

    [Fact]
    public async Task GivenALink_WhenItIsAddedOrRemovedByEntityRawSqlOrExecuteDelete_ThenTheItemChanges()
    {
        var s = await NewSetupAsync();
        await AssertChangedAsync(s.StoryItem, async db =>
        {
            db.MediaLinks.Add(MediaLink.ForTank(s.Tank.Id, s.StoryItem, MediaLinkRole.Decoration));
            await db.SaveChangesAsync();
        });
        await SettleAsync(s.StoryItem);
        await AssertChangedAsync(s.StoryItem, db => db.MediaLinks.Where(l => l.MediaItemId == s.StoryItem && l.TankId != null).ExecuteDeleteAsync());
        await SettleAsync(s.StoryItem);
        await AssertChangedAsync(s.StoryItem, db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO public.media_link (media_item_id, context_type, tank_id, role, position) VALUES ({s.StoryItem}, 'tank', {s.Tank.Id}, 'technic', 0)"));
    }

    [Fact]
    public async Task GivenAContext_WhenItIsDeleted_ThenTheCascadeOfItsLinksChangesTheItems()
    {
        var s = await NewSetupAsync();
        await AssertChangedAsync(s.EntryItem, db => db.CareEntries.Where(e => e.Id == s.Entry.Id).ExecuteDeleteAsync());
        await AssertChangedAsync(s.StoryItem, db => db.Stories.Where(p => p.Id == s.Story.Id).ExecuteDeleteAsync());
        await AssertChangedAsync(s.TankItem, db => db.Tanks.Where(t => t.Id == s.Tank.Id).ExecuteDeleteAsync());
    }

    [Fact]
    public async Task GivenAVisibilityCheckThatRunsWhileAChangeIsUncommitted_ThenItsGenerationIsStaleOnceTheChangeCommits()
    {
        var s = await NewSetupAsync();
        var viewer = await TestDataBuilder.CreateProfileAsync(fixture.CreateContext());
        await using var changing = fixture.CreateContext();
        await using var transaction = await changing.Database.BeginTransactionAsync();
        await changing.Tanks.Where(t => t.Id == s.Tank.Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.ProfileVisibility, TankVisibility.Private).SetProperty(t => t.StreamVisibility, TankVisibility.Private));

        await using (var issuing = fixture.CreateContext())
        {
            var issued = await issuing.VisibleMediaGenerationsAsync([s.TankItem], viewer.Id, DateTimeOffset.UtcNow);
            await transaction.CommitAsync();

            // The check saw the state before the change, so the generation it read is the one the change replaces.
            var generation = Assert.Single(issued).Value;
            Assert.NotEqual(generation, (await StateAsync(s.TankItem)).Generation);
        }

        await using var after = fixture.CreateContext();
        Assert.Empty(await after.VisibleMediaGenerationsAsync([s.TankItem], viewer.Id, DateTimeOffset.UtcNow));
        var owner = await after.VisibleMediaGenerationsAsync([s.TankItem], s.Owner.Id, DateTimeOffset.UtcNow);
        Assert.Equal((await StateAsync(s.TankItem)).Generation, Assert.Single(owner).Value);
    }

    [Fact]
    public async Task GivenParallelChangesOfDifferentContexts_ThenNoneFailsAndEveryEarlierGenerationIsStale()
    {
        var s = await NewSetupAsync();
        var before = (await StateAsync(s.EntryItem)).Generation;
        var issued = new System.Collections.Concurrent.ConcurrentBag<long>();

        async Task RunAsync(int i)
        {
            await using var db = fixture.CreateContext();
            if (i % 3 == 0) await db.Tanks.Where(t => t.Id == s.Tank.Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.StreamVisibility, i % 2 == 0 ? TankVisibility.Private : TankVisibility.Public));
            else if (i % 3 == 1) await db.CareEntries.Where(e => e.Id == s.Entry.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.OnlyMe, i % 2 == 0));
            else
            {
                var generations = await db.VisibleMediaGenerationsAsync([s.EntryItem], s.Owner.Id, DateTimeOffset.UtcNow);
                issued.Add(generations[s.EntryItem]);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() => RunAsync(i))));

        var last = (await StateAsync(s.EntryItem)).Generation;
        Assert.True(last > before);
        await using var db = fixture.CreateContext();
        await db.CareEntries.Where(e => e.Id == s.Entry.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.OnlyMe, false));
        await db.CareEntries.Where(e => e.Id == s.Entry.Id).ExecuteUpdateAsync(x => x.SetProperty(e => e.OnlyMe, true));
        var final = (await StateAsync(s.EntryItem)).Generation;
        Assert.All(issued, generation => Assert.True(generation < final));
    }

    [Fact]
    public async Task GivenATank_WhenAFollowIsApprovedOrRemoved_ThenItsItemsChange()
    {
        var s = await NewSetupAsync();
        await using var setup = fixture.CreateContext();
        var follower = await TestDataBuilder.CreateProfileAsync(setup);
        var requester = await TestDataBuilder.CreateProfileAsync(setup);

        await AssertChangedAsync(s.TankItem, async db =>
        {
            db.Follows.Add(new Follow { FollowerProfileId = follower.Id, TargetTankId = s.Tank.Id, Status = FollowStatus.Approved, CreatedAt = Now });
            await db.SaveChangesAsync();
        });
        foreach (var item in new[] { s.EntryItem, s.SessionItem }) Assert.True((await StateAsync(item)).Pending);
        await SettleAsync(s.TankItem, s.EntryItem, s.SessionItem);

        await AssertUnchangedAsync(s.TankItem, async db =>
        {
            db.Follows.Add(new Follow { FollowerProfileId = requester.Id, TargetTankId = s.Tank.Id, Status = FollowStatus.Pending, CreatedAt = Now });
            await db.SaveChangesAsync();
        });
        await AssertChangedAsync(s.TankItem, db => db.Follows.Where(f => f.FollowerProfileId == requester.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(f => f.Status, FollowStatus.Approved)));
        await SettleAsync(s.TankItem);
        await AssertChangedAsync(s.TankItem, db => db.Follows.Where(f => f.FollowerProfileId == follower.Id).ExecuteDeleteAsync());
        await SettleAsync(s.TankItem);
        await AssertChangedAsync(s.TankItem, db => db.Follows.Where(f => f.FollowerProfileId == requester.Id).ExecuteDeleteAsync());
    }

    [Fact]
    public async Task GivenAPendingFollow_WhenItIsDeleted_ThenNothingChanges()
    {
        var s = await NewSetupAsync();
        await using var setup = fixture.CreateContext();
        var requester = await TestDataBuilder.CreateProfileAsync(setup);
        setup.Follows.Add(new Follow { FollowerProfileId = requester.Id, TargetTankId = s.Tank.Id, Status = FollowStatus.Pending, CreatedAt = Now });
        await setup.SaveChangesAsync();
        await SettleAsync(s.TankItem);
        await AssertUnchangedAsync(s.TankItem, db => db.Follows.Where(f => f.FollowerProfileId == requester.Id).ExecuteDeleteAsync());
    }

    [Fact]
    public async Task GivenAProfileFollow_ThenNoTankItemChanges()
    {
        var s = await NewSetupAsync();
        await using var setup = fixture.CreateContext();
        var follower = await TestDataBuilder.CreateProfileAsync(setup);
        await AssertUnchangedAsync(s.TankItem, async db =>
        {
            db.Follows.Add(new Follow { FollowerProfileId = follower.Id, TargetProfileId = s.Owner.Id, CreatedAt = Now });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task GivenATank_WhenASelectedMemberIsAddedOrRemoved_ThenItsItemsChange()
    {
        var s = await NewSetupAsync();
        await using var setup = fixture.CreateContext();
        var member = await TestDataBuilder.CreateProfileAsync(setup);

        await AssertChangedAsync(s.TankItem, async db =>
        {
            db.TankSelectedMembers.Add(new TankSelectedMember { TankId = s.Tank.Id, MemberProfileId = member.Id, AddedAt = Now });
            await db.SaveChangesAsync();
        });
        foreach (var item in new[] { s.EntryItem, s.SessionItem }) Assert.True((await StateAsync(item)).Pending);
        await SettleAsync(s.TankItem);
        await AssertChangedAsync(s.TankItem, db => db.TankSelectedMembers.Where(m => m.MemberProfileId == member.Id).ExecuteDeleteAsync());
    }
}

