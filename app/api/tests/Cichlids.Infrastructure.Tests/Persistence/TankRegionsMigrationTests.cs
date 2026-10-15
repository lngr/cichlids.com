using Cichlids.Domain;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Cichlids.Infrastructure.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class TankRegionsMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task MapsEveryLegacyCategoryAndInitializesProfileSets()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261015092008_TankMetricAndInputMeasurements");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO public.profile (id, username, kind, created_at) VALUES (1, 'regions', 'member', now());
            INSERT INTO public.tank (profile_id, title, category, state, created_at)
            SELECT 1, category, category, 'published', now()
            FROM unnest(ARRAY['malawi', 'tanganyika', 'african', 'community', 'central_american', 'south_american', 'american']) category;
            """);
        await migrator.MigrateAsync();
        var tanks = await context.Tanks.ToListAsync();
        Assert.Equal(7, tanks.Count);
        foreach (var tank in tanks) Assert.Equal(TankRegions.FromLegacyCategory(tank.Category), tank.Region);
        Assert.Equal(TankRegion.AmericasUnassigned, tanks.Single(t => t.Category == TankCategory.American).Region);
        var profile = await context.Profiles.SingleAsync();
        Assert.Empty(profile.Interests);
        Assert.Empty(profile.Badges);
        Assert.Null(profile.OnboardingCompletedAt);
    }
}
