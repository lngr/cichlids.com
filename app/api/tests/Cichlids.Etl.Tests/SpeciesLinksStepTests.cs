using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(EtlCollection.Name)]
public sealed class SpeciesLinksStepTests(EtlFixture fixture)
{
    [Fact]
    public async Task MigratesPictureSpeciesLinksToStorySubjectsIdempotently()
    {
        await RunPrerequisiteStepsAsync();

        var stats1 = await RunStepAsync();

        Assert.Equal(5, stats1.Read);
        Assert.Equal(2, stats1.Inserted);
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("species_link_species_zero"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("species_link_story_not_migrated"));
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("species_link_duplicate"));

        await using (var db = fixture.CreateTargetContext())
        {
            var story4801 = await db.Stories.SingleAsync(p => p.LegacyId == 4801);
            var species4810 = await db.Species.SingleAsync(s => s.LegacyId == 4810);
            var species4811 = await db.Species.SingleAsync(s => s.LegacyId == 4811);

            var links = await db.StorySubjects.Where(x => x.StoryId == story4801.Id).OrderBy(x => x.Id).ToListAsync();
            Assert.Equal([species4810.Id, species4811.Id], links.Select(x => x.SpeciesId!.Value));
            Assert.All(links, x =>
            {
                Assert.Equal(StorySubjectType.Species, x.SubjectType);
                Assert.Null(x.Position);
            });
        }

        var stats2 = await RunStepAsync();

        Assert.Equal(5, stats2.Read);
        Assert.Equal(2, stats2.Inserted);
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("species_link_species_zero"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("species_link_story_not_migrated"));
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("species_link_duplicate"));

        await using (var db = fixture.CreateTargetContext())
        {
            var story4801 = await db.Stories.SingleAsync(p => p.LegacyId == 4801);
            Assert.Equal(2, await db.StorySubjects.CountAsync(x => x.StoryId == story4801.Id));
        }
    }

    // Owner legacy id 480, story legacy ids 4801/4802 and species legacy ids 4810/4811 are their
    // own range, disjoint from every other step test's fixtures. Seeded directly (idempotent
    // find-or-create), the same way the other step tests seed their own prerequisites, instead of
    // running ProfileMigrationStep/PictureMigrationStep/SpeciesCatalogStep.
    private Task RunPrerequisiteStepsAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var db = fixture.CreateTargetContext();

        if (!await db.Profiles.AnyAsync(p => p.LegacyId == 480))
        {
            db.Profiles.Add(new Profile
            {
                LegacyId = 480,
                Username = "species-links-fixture-owner-480",
                DisplayName = "Species Links Fixture Owner 480",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        await db.SaveChangesAsync();

        var owner = await db.Profiles.SingleAsync(p => p.LegacyId == 480);

        if (!await db.Stories.AnyAsync(p => p.LegacyId == 4801))
        {
            db.Stories.Add(new Story
            {
                LegacyId = 4801,
                AuthorProfileId = owner.Id,
                Topic = StoryTopic.Cichlids,
                State = StoryState.Published,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        if (!await db.Stories.AnyAsync(p => p.LegacyId == 4802))
        {
            db.Stories.Add(new Story
            {
                LegacyId = 4802,
                AuthorProfileId = owner.Id,
                Topic = StoryTopic.Cichlids,
                State = StoryState.Published,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        if (!await db.Species.AnyAsync(s => s.LegacyId == 4810))
        {
            db.Species.Add(new Species
            {
                LegacyId = 4810,
                Genus = "Speciesus",
                Name = "linksfixtureone",
                DisplayName = "Speciesus linksfixtureone",
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        if (!await db.Species.AnyAsync(s => s.LegacyId == 4811))
        {
            db.Species.Add(new Species
            {
                LegacyId = 4811,
                Genus = "Speciesus",
                Name = "linksfixturetwo",
                DisplayName = "Speciesus linksfixturetwo",
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1700000000),
            });
        }

        await db.SaveChangesAsync();
    });

    private Task<StepStatistics> RunStepAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, CancellationToken.None);
        await EtlRunner.RunAsync(new SpeciesLinksStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("species-links");
    });
}
