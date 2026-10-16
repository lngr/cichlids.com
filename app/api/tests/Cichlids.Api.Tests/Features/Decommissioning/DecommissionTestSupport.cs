using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Tests.Features.Decommissioning;

/// <summary>
/// Seeds decommissionings straight into the database so their dates can lie in the past.
/// </summary>
internal static class DecommissionTestSupport
{
    public static DateTimeOffset At(DateOnly date, int hour = 12) => new(date.Year, date.Month, date.Day, hour, 0, 0, TimeSpan.Zero);

    public static async Task<long> AddAsync(ApiFixture fixture, long tank, long profile, DecommissionScope scope, DateOnly start, DateOnly? end)
    {
        await using var db = fixture.CreateDbContext();
        var startPhoto = await PhotoAsync(db, profile);
        var row = new TankDecommissioning
        {
            TankId = tank, Scope = scope, StartedAt = At(start), StartDate = start, StartMediaItemId = startPhoto,
        };
        if (end is { } last)
        {
            row.EndedAt = At(last);
            row.EndDate = last;
            row.EndMediaItemId = await PhotoAsync(db, profile);
        }
        db.TankDecommissionings.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private static async Task<long> PhotoAsync(Cichlids.Infrastructure.Persistence.CichlidsDbContext db, long profile)
    {
        var item = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, OwnerProfileId = profile, CreatedAt = DateTimeOffset.UtcNow };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }
}
