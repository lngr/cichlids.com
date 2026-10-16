using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Tests.Persistence;

/// <summary>
/// A link that is created or removed marks its media item for placement in the transaction that saves
/// the link change.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MediaPlacementMarksTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<(long Item, long Tank)> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        var owner = await TestDataBuilder.CreateProfileAsync(db);
        var tank = await TestDataBuilder.CreateTankAsync(db, owner.Id);
        var item = new MediaItem { OwnerProfileId = owner.Id, Kind = MediaKind.Photo, StorageKey = $"originals/{Guid.NewGuid():N}.jpg", CreatedAt = Now, ReceivedAt = Now };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        return (item.Id, tank.Id);
    }

    private async Task<MediaItem> StoredAsync(long id)
    {
        await using var db = fixture.CreateContext();
        return await db.MediaItems.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private async Task SettleAsync(long id)
    {
        await using var db = fixture.CreateContext();
        await db.MediaItems.Where(m => m.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.PlacementPending, false).SetProperty(m => m.PlacementAttempts, 3)
            .SetProperty(m => m.PlacementNextAttemptAt, Now.AddHours(1)));
    }

    [Fact]
    public async Task GivenAnUntrackedItem_WhenALinkIsAdded_ThenTheItemIsMarkedAndNothingElseOfItIsWritten()
    {
        var (item, tank) = await SeedAsync();
        await SettleAsync(item);
        await using var db = fixture.CreateContext();

        db.MediaLinks.Add(MediaLink.ForTank(tank, item, MediaLinkRole.Showcase));
        await db.SaveChangesAsync();

        var stored = await StoredAsync(item);
        Assert.True(stored.PlacementPending);
        Assert.Equal(0, stored.PlacementAttempts);
        Assert.Null(stored.PlacementNextAttemptAt);
        Assert.StartsWith("originals/", stored.StorageKey);
        Assert.Empty(db.ChangeTracker.Entries<MediaItem>());
    }

    [Fact]
    public async Task GivenALink_WhenItIsRemoved_ThenTheItemIsMarked()
    {
        var (item, tank) = await SeedAsync();
        await using (var seed = fixture.CreateContext())
        {
            seed.MediaLinks.Add(MediaLink.ForTank(tank, item, MediaLinkRole.Showcase));
            await seed.SaveChangesAsync();
        }

        await SettleAsync(item);
        await using var db = fixture.CreateContext();
        var link = await db.MediaLinks.SingleAsync(l => l.MediaItemId == item);
        db.MediaLinks.Remove(link);
        await db.SaveChangesAsync();

        Assert.True((await StoredAsync(item)).PlacementPending);
    }

    [Fact]
    public async Task GivenATrackedItem_WhenALinkIsAddedThroughTheItem_ThenTheTrackedItemIsMarked()
    {
        var (item, tank) = await SeedAsync();
        await SettleAsync(item);
        await using var db = fixture.CreateContext();
        var tracked = await db.MediaItems.SingleAsync(m => m.Id == item);

        db.MediaLinks.Add(new MediaLink { ContextType = MediaLinkContext.Tank, TankId = tank, MediaItem = tracked, Role = MediaLinkRole.Showcase });
        await db.SaveChangesAsync();

        Assert.True(tracked.PlacementPending);
        Assert.True((await StoredAsync(item)).PlacementPending);
    }

    [Fact]
    public async Task GivenANewItemWithALink_WhenItIsSaved_ThenItIsPending()
    {
        var (_, tank) = await SeedAsync();
        await using var db = fixture.CreateContext();
        var fresh = new MediaItem { Kind = MediaKind.Photo, StorageKey = $"originals/{Guid.NewGuid():N}.jpg", CreatedAt = Now, ReceivedAt = Now };

        db.MediaLinks.Add(new MediaLink { ContextType = MediaLinkContext.Tank, TankId = tank, MediaItem = fresh, Role = MediaLinkRole.Showcase });
        await db.SaveChangesAsync();

        Assert.True((await StoredAsync(fresh.Id)).PlacementPending);
    }

    [Fact]
    public async Task GivenAnItemThatIsDeletedWithItsLink_WhenSaved_ThenTheDeletionSucceeds()
    {
        var (item, tank) = await SeedAsync();
        await using (var seed = fixture.CreateContext())
        {
            seed.MediaLinks.Add(MediaLink.ForTank(tank, item, MediaLinkRole.Showcase));
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateContext();
        var link = await db.MediaLinks.Include(l => l.MediaItem).SingleAsync(l => l.MediaItemId == item);
        db.MediaLinks.Remove(link);
        db.MediaItems.Remove(link.MediaItem);
        await db.SaveChangesAsync();

        Assert.False(await db.MediaItems.AnyAsync(m => m.Id == item));
    }

    [Fact]
    public async Task GivenTheItemsOfATank_WhenMarkedTogether_ThenOnlyThoseOfTheTankAreMarked()
    {
        var (item, tank) = await SeedAsync();
        var (other, _) = await SeedAsync();
        await using (var seed = fixture.CreateContext())
        {
            seed.MediaLinks.Add(MediaLink.ForTank(tank, item, MediaLinkRole.Showcase));
            await seed.SaveChangesAsync();
        }

        await SettleAsync(item);
        await SettleAsync(other);
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.MarkForPlacementAsync(db.MediaItemIdsOfTank(tank)));

        Assert.True((await StoredAsync(item)).PlacementPending);
        Assert.False((await StoredAsync(other)).PlacementPending);
    }
}
