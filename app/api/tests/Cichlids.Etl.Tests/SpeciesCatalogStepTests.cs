using Cichlids.Domain.Enums;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(EtlCollection.Name)]
public sealed class SpeciesCatalogStepTests(EtlFixture fixture)
{
    [Fact]
    public async Task MigratesSpeciesCatalogIdempotently()
    {
        var stats1 = await RunStepAsync();

        Assert.Equal(4, stats1.Read); // legacy uid 5 is soft-deleted and excluded.
        Assert.Equal(4, stats1.Inserted);
        Assert.Equal(0, stats1.Updated);
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("species_breeding_unknown_value_9"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("species_aggro_unknown_value_9"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("species_inner_aggro_unknown_value_9"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("species_diet_unknown_value_9"));
        var collisionWarning = Assert.Single(stats1.Warnings);
        Assert.Contains("collision_slug", collisionWarning);

        await using (var db = fixture.CreateTargetContext())
        {
            var withFallback = await db.Species
                .Include(s => s.CommonNames)
                .Include(s => s.Links)
                .SingleAsync(s => s.LegacyId == 1);
            Assert.Equal("Testonoma testus", withFallback.DisplayName);
            Assert.Equal("lake test", withFallback.Category);
            Assert.Equal("testonoma_testus", withFallback.Slug);
            Assert.Equal(SpeciesBreeding.Mouthbreeder, withFallback.Breeding);
            Assert.Equal(AggressionLevel.Moderate, withFallback.Aggression);
            Assert.Equal(AggressionLevel.Low, withFallback.IntraAggression);
            Assert.Equal(SpeciesDiet.Carnivore, withFallback.Diet);
            var commonName = Assert.Single(withFallback.CommonNames);
            Assert.Equal("Test Cichlid", commonName.Name);
            Assert.Equal(2, withFallback.Links.Count);
            var firstLink = withFallback.Links.Single(l => l.Sort == 0);
            Assert.Equal("http://a.example", firstLink.Url);
            Assert.Equal("LinkA", firstLink.Label);
            var secondLink = withFallback.Links.Single(l => l.Sort == 1);
            Assert.Equal("http://b.example", secondLink.Url);
            Assert.Null(secondLink.Label);

            var broken = await db.Species.SingleAsync(s => s.LegacyId == 2);
            Assert.Equal(SpeciesBreeding.Unspecified, broken.Breeding);
            Assert.Equal(AggressionLevel.Unspecified, broken.Aggression);
            Assert.Equal(AggressionLevel.Unspecified, broken.IntraAggression);
            Assert.Equal(SpeciesDiet.Unspecified, broken.Diet);
            Assert.Null(broken.Category);
            Assert.Null(broken.Slug);

            var three = await db.Species.SingleAsync(s => s.LegacyId == 3);
            var four = await db.Species.SingleAsync(s => s.LegacyId == 4);
            Assert.Equal("collision_slug", three.Slug);
            Assert.Equal("collision_slug-4", four.Slug);

            Assert.False(await db.Species.AnyAsync(s => s.LegacyId == 5));
        }

        DateTimeOffset createdAtAfterFirstRun;
        await using (var db = fixture.CreateTargetContext())
        {
            createdAtAfterFirstRun = (await db.Species.SingleAsync(s => s.LegacyId == 1)).CreatedAt;
        }

        // The step computes a fresh DateTimeOffset.UtcNow for created_at on every run; the upsert
        // must still keep the first run's value on the row instead of overwriting it.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        var stats2 = await RunStepAsync();

        Assert.Equal(4, stats2.Read);
        Assert.Equal(0, stats2.Inserted);
        Assert.Equal(4, stats2.Updated);

        await using (var db = fixture.CreateTargetContext())
        {
            // Scoped to this fixture's own legacy id range: TankMigrationStepTests seeds an
            // unrelated species row (legacy id 101) directly into the same shared table.
            Assert.Equal(4, await db.Species.CountAsync(s => s.LegacyId <= 5));
            Assert.Equal(1, await db.SpeciesCommonNames.CountAsync());
            Assert.Equal(2, await db.SpeciesLinks.CountAsync());

            var afterSecondRun = await db.Species.SingleAsync(s => s.LegacyId == 1);
            Assert.Equal(createdAtAfterFirstRun, afterSecondRun.CreatedAt);
        }
    }

    private Task<StepStatistics> RunStepAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, dryRun: false, CancellationToken.None);
        await EtlRunner.RunAsync(new SpeciesCatalogStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("species");
    });
}
