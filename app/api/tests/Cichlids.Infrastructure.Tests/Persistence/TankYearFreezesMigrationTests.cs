using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class TankYearFreezesMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task StoredFreezesGetTheTankYearOfTheirPeriodEnd()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261016085020_Streaks");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO public.profile (id, username, kind, created_at) VALUES (1, 'before', 'member', now());
            INSERT INTO public.tank (id, profile_id, title, state, created_at) VALUES (1, 1, 'March tank', 'published', '2026-03-01T10:00:00Z');
            INSERT INTO public.tank (id, profile_id, title, state, created_at) VALUES (2, 1, 'Leap day tank', 'published', '2024-02-29T10:00:00Z');
            INSERT INTO public.care_entry (id, tank_id, author_profile_id, kind, occurred_at, recorded_at)
            SELECT n, CASE WHEN n <= 4 THEN 1 ELSE 2 END, 1, 'water_change', now(), now() FROM generate_series(1, 7) AS n;
            INSERT INTO public.streak_freeze (id, tank_id, care_entry_id, year, period_start, period_end, created_at) VALUES
              (1, 1, 1, 2026, '2026-03-20', '2026-03-27', now()),
              (2, 1, 2, 2027, '2027-02-20', '2027-02-28', now()),
              (3, 1, 3, 2027, '2027-03-01', '2027-03-08', now()),
              (4, 1, 4, 2026, '2026-02-10', '2026-02-17', now()),
              (5, 2, 5, 2025, '2025-02-20', '2025-02-27', now()),
              (6, 2, 6, 2025, '2025-02-28', '2025-03-07', now()),
              (7, 2, 7, 2028, '2028-02-20', '2028-02-28', now());
            """);

        await migrator.MigrateAsync();

        var years = await context.Database.SqlQueryRaw<string>("SELECT tank_year::text AS \"Value\" FROM public.streak_freeze ORDER BY id").ToListAsync();
        Assert.Equal(["1", "1", "2", "1", "1", "2", "4"], years);
    }
}
