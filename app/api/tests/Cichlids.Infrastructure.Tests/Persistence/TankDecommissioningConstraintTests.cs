using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Trait("Category", "Docker")]
[Collection(PostgresCollection.Name)]
public sealed class TankDecommissioningConstraintTests(PostgresFixture fixture)
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    private async Task<(long Tank, long Photo)> TankAsync()
    {
        await using var context = fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        var tank = await TestDataBuilder.CreateTankAsync(context, profile.Id);
        return (tank.Id, await PhotoAsync(profile.Id));
    }

    private async Task<long> PhotoAsync(long profile)
    {
        await using var context = fixture.CreateContext();
        var item = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, OwnerProfileId = profile, CreatedAt = DateTimeOffset.UtcNow };
        context.MediaItems.Add(item);
        await context.SaveChangesAsync();
        return item.Id;
    }

    private static TankDecommissioning Open(long tank, long photo, DecommissionScope scope = DecommissionScope.AllCare) =>
        new() { TankId = tank, Scope = scope, StartedAt = DateTimeOffset.UtcNow, StartDate = Day, StartMediaItemId = photo };

    [Fact]
    public async Task A_decommissioning_is_stored_open_and_closed_with_its_scope_as_text()
    {
        var (tank, photo) = await TankAsync();
        var endPhoto = await PhotoAsync((await OwnerAsync(tank)));
        await using var context = fixture.CreateContext();
        var closed = Open(tank, photo, DecommissionScope.WaterChange);
        closed.EndedAt = DateTimeOffset.UtcNow;
        closed.EndDate = Day.AddDays(3);
        closed.EndMediaItemId = endPhoto;
        context.TankDecommissionings.Add(closed);
        context.TankDecommissionings.Add(Open(tank, photo));
        await context.SaveChangesAsync();
        Assert.Equal(["all_care", "water_change"],
            (await context.Database.SqlQuery<string>($"SELECT scope AS \"Value\" FROM public.tank_decommissioning WHERE tank_id = {tank} ORDER BY scope").ToListAsync()));
    }

    [Fact]
    public async Task A_tank_has_at_most_one_active_decommissioning()
    {
        var (tank, photo) = await TankAsync();
        await using var context = fixture.CreateContext();
        context.TankDecommissionings.Add(Open(tank, photo));
        await context.SaveChangesAsync();
        await using var second = fixture.CreateContext();
        second.TankDecommissionings.Add(Open(tank, photo));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task An_end_needs_its_date_its_instant_and_its_photo_together_and_must_not_precede_the_start()
    {
        var (tank, photo) = await TankAsync();
        await using var half = fixture.CreateContext();
        var partial = Open(tank, photo);
        partial.EndedAt = DateTimeOffset.UtcNow;
        half.TankDecommissionings.Add(partial);
        await Assert.ThrowsAsync<DbUpdateException>(() => half.SaveChangesAsync());

        await using var backwards = fixture.CreateContext();
        var early = Open(tank, photo);
        early.EndedAt = DateTimeOffset.UtcNow;
        early.EndDate = Day.AddDays(-1);
        early.EndMediaItemId = photo;
        backwards.TankDecommissionings.Add(early);
        await Assert.ThrowsAsync<DbUpdateException>(() => backwards.SaveChangesAsync());
    }

    [Fact]
    public async Task The_photos_of_a_decommissioning_are_tank_media_of_their_own_sections()
    {
        var (tank, photo) = await TankAsync();
        await using var context = fixture.CreateContext();
        context.TankMedia.Add(new TankMedia { TankId = tank, MediaItemId = photo, Section = TankMediaSection.Decommission, CapturedInApp = true });
        context.TankMedia.Add(new TankMedia { TankId = tank, MediaItemId = photo, Section = TankMediaSection.Recommission, CapturedInApp = true });
        await context.SaveChangesAsync();
        Assert.Equal(2, await context.TankMedia.CountAsync(m => m.TankId == tank));
    }

    [Fact]
    public async Task Deleting_the_tank_removes_its_decommissionings()
    {
        var (tank, photo) = await TankAsync();
        await using (var context = fixture.CreateContext())
        {
            context.TankDecommissionings.Add(Open(tank, photo));
            await context.SaveChangesAsync();
        }
        await using var verify = fixture.CreateContext();
        await verify.Tanks.Where(t => t.Id == tank).ExecuteDeleteAsync();
        Assert.False(await verify.TankDecommissionings.AnyAsync(d => d.TankId == tank));
    }

    private async Task<long> OwnerAsync(long tank)
    {
        await using var context = fixture.CreateContext();
        return await context.Tanks.Where(t => t.Id == tank).Select(t => t.ProfileId).SingleAsync();
    }
}
