using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class TankUnitsMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task PreservesLegacyInputAndConvertsExistingInchesOnce()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261013120319_EncryptWebSessionTokens");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO public.profile (id, username, display_name, kind, created_at)
            VALUES (1, 'units-migration', 'Units', 'member', now());
            INSERT INTO public.tank (id, profile_id, title, state, created_at, width_value, height_value, dimension_unit, water_gh)
            VALUES (1, 1, 'Inches', 'published', now(), 48, 20, 'inch', '10-15 dGH'),
                   (2, 1, 'Metric', 'published', now(), 120, NULL, 'cm', NULL),
                   (3, 1, 'Unknown', 'draft', now(), NULL, NULL, NULL, NULL);
            """);
        await migrator.MigrateAsync();
        var tanks = await context.Tanks.OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(121.92m, tanks[0].WidthValue);
        Assert.Equal(48m, tanks[0].WidthInputValue);
        Assert.Equal(50.8m, tanks[0].HeightValue);
        Assert.Equal(20m, tanks[0].HeightInputValue);
        Assert.Equal(DimensionUnit.Inch, tanks[0].DimensionUnit);
        Assert.Equal("10-15 dGH", tanks[0].WaterGh);
        Assert.Null(tanks[0].DepthValue);
        Assert.Null(tanks[0].VolumeLiters);
        Assert.Equal(120m, tanks[1].WidthValue);
        Assert.Equal(120m, tanks[1].WidthInputValue);
        Assert.Null(tanks[2].WidthInputValue);
        await migrator.MigrateAsync();
        Assert.Equal(121.92m, await context.Tanks.Where(t => t.Id == 1).Select(t => t.WidthValue).SingleAsync());
    }
}
