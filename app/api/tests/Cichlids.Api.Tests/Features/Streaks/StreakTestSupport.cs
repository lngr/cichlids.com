using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Tests.Features.Streaks;

/// <summary>
/// Seeds the history the streak tests read: water changes with an evidence state, photos of the
/// time-lapse series and vacations, written straight to the database so the dates can lie in the past.
/// </summary>
internal static class StreakTestSupport
{
    public const string Today = "2026-10-15";

    public static DateTimeOffset At(int month, int day, int hour = 12) => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

    public static async Task<long> AddWaterChangeAsync(ApiFixture fixture, long tank, long profile, DateTimeOffset at, bool verified)
    {
        await using var db = fixture.CreateDbContext();
        var entry = new CareEntry { TankId = tank, AuthorProfileId = profile, Kind = CareKind.WaterChange, OccurredAt = at, RecordedAt = at };
        if (verified)
        {
            var session = new WaterChangeSession
            {
                TankId = tank, ProfileId = profile, StartedAt = at.AddMinutes(-20), FinishedAt = at, DurationSeconds = 1200,
            };
            db.WaterChangeSessions.Add(session);
            await db.SaveChangesAsync();
            entry.WaterChangeSessionId = session.Id;
            entry.DurationSeconds = 1200;
            entry.Evidence = WaterChangeEvidenceState.Verified;
        }
        db.CareEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    public static async Task<long> AddSeriesPhotoAsync(ApiFixture fixture, long tank, long profile, DateTimeOffset receivedAt, bool capturedInApp = true)
    {
        await using var db = fixture.CreateDbContext();
        var item = new MediaItem
        {
            StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, OwnerProfileId = profile, CreatedAt = receivedAt, ReceivedAt = receivedAt,
            CapturedInApp = capturedInApp,
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        db.MediaLinks.Add(MediaLink.ForTank(tank, item.Id, MediaLinkRole.TimeLapse));
        await db.SaveChangesAsync();
        return item.Id;
    }

    public static async Task AddVacationAsync(ApiFixture fixture, long profile, DateOnly start, DateOnly? end)
    {
        await using var db = fixture.CreateDbContext();
        db.Vacations.Add(new Vacation { ProfileId = profile, StartDate = start, EndDate = end, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Removes the outbox events of the given aggregate types and the tanks of the members the streak
    /// tests created, so tests that count tanks of the shared database see none of them.
    /// </summary>
    public static async Task RemoveEventsAsync(ApiFixture fixture, params string[] aggregateTypes)
    {
        await using var db = fixture.CreateDbContext();
        await db.OutboxEvents.Where(e => aggregateTypes.Contains(e.AggregateType)).ExecuteDeleteAsync();
        var tanks = await db.Tanks.Where(t => db.Profiles.Any(p => p.Id == t.ProfileId && p.Username.StartsWith("vis-"))).Select(t => t.Id).ToListAsync();
        await db.OutboxEvents.Where(e => e.AggregateType == "tank" && tanks.Select(id => id.ToString()).Contains(e.AggregateId)).ExecuteDeleteAsync();
        await db.CarePlanRules.Where(r => tanks.Contains(r.TankId)).ExecuteDeleteAsync();
        await db.CareEntries.Where(e => tanks.Contains(e.TankId)).ExecuteDeleteAsync();
        await db.Tanks.Where(t => tanks.Contains(t.Id)).ExecuteDeleteAsync();
    }
}
