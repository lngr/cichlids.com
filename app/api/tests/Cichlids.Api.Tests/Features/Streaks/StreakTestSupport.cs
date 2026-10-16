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
        var item = new MediaItem { StorageKey = Guid.NewGuid().ToString(), Kind = MediaKind.Photo, OwnerProfileId = profile, CreatedAt = receivedAt };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        db.TankMedia.Add(new TankMedia { TankId = tank, MediaItemId = item.Id, Section = TankMediaSection.TimeLapse, CapturedInApp = capturedInApp });
        await db.SaveChangesAsync();
        return item.Id;
    }

    public static async Task AddVacationAsync(ApiFixture fixture, long profile, DateOnly start, DateOnly? end)
    {
        await using var db = fixture.CreateDbContext();
        db.Vacations.Add(new Vacation { ProfileId = profile, StartDate = start, EndDate = end, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public static async Task RemoveEventsAsync(ApiFixture fixture, params string[] aggregateTypes)
    {
        await using var db = fixture.CreateDbContext();
        await db.OutboxEvents.Where(e => aggregateTypes.Contains(e.AggregateType)).ExecuteDeleteAsync();
    }
}
