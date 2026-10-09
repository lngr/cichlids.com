using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(EtlCollection.Name)]
public sealed class TankMigrationStepTests(EtlFixture fixture)
{
    [Fact]
    public async Task MigratesTanksWithInhabitantsIdempotently()
    {
        await RunPrerequisiteStepsAsync();

        var stats1 = await RunStepAsync();

        // uid 1 and 2 are soft-deleted in the fixture and never read by this step; uid 10-14 are
        // five active tanks owned by this test class, and uid 300 is PictureMigrationStepTests'
        // tank_media fixture (read unconditionally by this step too, since it scans the whole
        // table, though this step never sets its media fields). Whether uid 300 itself lands as
        // an insert or an update here depends on whether PictureMigrationStepTests' own
        // prerequisite happened to seed that same legacy id into the target first, so only the
        // total is asserted rather than the exact split; every other row in this run's own count
        // is deterministic.
        Assert.Equal(6, stats1.Read);
        Assert.Equal(6, stats1.Inserted + stats1.Updated);
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("tank_category_unmapped_99"));
        Assert.Equal(3, stats1.SkipReasons.GetValueOrDefault("tank_inhabitant_species_missing"));
        Assert.Contains(stats1.Warnings, w => w.Contains("legacy user 999"));
        Assert.Contains(stats1.Warnings, w => w.Contains("fe_user is 0"));

        await using (var db = fixture.CreateTargetContext())
        {
            var owner = await db.Profiles.SingleAsync(p => p.LegacyId == 101);

            var reef = await db.Tanks
                .Include(t => t.Inhabitants)
                .SingleAsync(t => t.LegacyId == 10);
            Assert.Equal(owner.Id, reef.ProfileId);
            Assert.Equal("Reef Tank", reef.Title);
            Assert.Equal(TankCategory.Tanganyika, reef.Category);
            Assert.Equal(TankState.Published, reef.State);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000000600), reef.PublishedAt);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000000500), reef.CreatedAt);
            Assert.Equal(100, reef.WidthValue);
            Assert.Equal(40, reef.HeightValue);
            Assert.Equal(40, reef.DepthValue);
            Assert.Equal(DimensionUnit.Cm, reef.DimensionUnit);
            Assert.Null(reef.MainMediaId);

            // Position 0 resolves to species legacy id 101; position 1 ("0") drops entirely
            // because it has neither a species nor a count; positions 2 and 3 keep a count-only
            // row because their species token was empty/unresolvable but a count was recorded.
            Assert.Equal(3, reef.Inhabitants.Count);
            var species = await db.Species.SingleAsync(s => s.LegacyId == 101);
            var first = reef.Inhabitants.Single(i => i.Sort == 0);
            Assert.Equal(species.Id, first.SpeciesId);
            Assert.Equal(5, first.Count);
            var third = reef.Inhabitants.Single(i => i.Sort == 2);
            Assert.Null(third.SpeciesId);
            Assert.Equal(3, third.Count);
            var fourth = reef.Inhabitants.Single(i => i.Sort == 3);
            Assert.Null(fourth.SpeciesId);
            Assert.Equal(2, fourth.Count);
            Assert.DoesNotContain(reef.Inhabitants, i => i.Sort == 1);

            var draft = await db.Tanks.SingleAsync(t => t.LegacyId == 11);
            Assert.Equal(TankState.Draft, draft.State);
            Assert.Null(draft.PublishedAt);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000000700), draft.CreatedAt);
            Assert.Null(draft.Category);
            Assert.Null(draft.WidthValue);
            Assert.Equal(50, draft.HeightValue);
            Assert.Null(draft.DepthValue);
            Assert.Equal(DimensionUnit.Inch, draft.DimensionUnit);

            var orphaned = await db.Tanks.SingleAsync(t => t.LegacyId == 12);
            var placeholder = await db.Profiles.SingleAsync(p => p.LegacyId == 999);
            Assert.Equal(placeholder.Id, orphaned.ProfileId);
            Assert.Equal(ProfileKind.Archived, placeholder.Kind);
            Assert.Equal("former-member-999", placeholder.Username);
            Assert.Equal("Former member", placeholder.DisplayName);
            Assert.Equal(DimensionUnit.Inch, orphaned.DimensionUnit);

            var inchesQuirk = await db.Tanks.SingleAsync(t => t.LegacyId == 13);
            Assert.Equal(DimensionUnit.Cm, inchesQuirk.DimensionUnit);

            var anonymous = await db.Tanks.SingleAsync(t => t.LegacyId == 14);
            var communityArchive = await db.Profiles.SingleAsync(p => p.Username == "community-archive");
            Assert.Equal(communityArchive.Id, anonymous.ProfileId);
            Assert.Equal(ProfileKind.System, communityArchive.Kind);
            Assert.Null(communityArchive.LegacyId);
        }

        var stats2 = await RunStepAsync();

        Assert.Equal(6, stats2.Read);
        Assert.Equal(0, stats2.Inserted);
        Assert.Equal(6, stats2.Updated);
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("tank_category_unmapped_99"));
        Assert.Equal(3, stats2.SkipReasons.GetValueOrDefault("tank_inhabitant_species_missing"));
        // The placeholder profile already exists on the second run, so no new one is created and
        // that warning does not fire again; the anonymous-owner warning is unconditional and does.
        Assert.DoesNotContain(stats2.Warnings, w => w.Contains("legacy user 999"));
        Assert.Contains(stats2.Warnings, w => w.Contains("fe_user is 0"));

        await using (var db = fixture.CreateTargetContext())
        {
            Assert.Equal(6, await db.Tanks.CountAsync(t => t.LegacyId != null));
            Assert.Equal(1, await db.Profiles.CountAsync(p => p.LegacyId == 999));
            Assert.Equal(1, await db.Profiles.CountAsync(p => p.Username == "community-archive"));

            var reef = await db.Tanks.Include(t => t.Inhabitants).SingleAsync(t => t.LegacyId == 10);
            Assert.Equal(3, reef.Inhabitants.Count);
        }
    }

    // TankMigrationStep resolves owners and inhabitants against whatever profile/species rows
    // already exist in the target, exactly like the real pipeline does after the species and
    // profile steps have run. This test seeds only the two rows it actually needs directly,
    // instead of running SpeciesCatalogStep/ProfileMigrationStep: those steps' own tests assert
    // exact inserted/updated counts against a pristine database, and this class shares the same
    // target Postgres instance across the whole test collection, so running the real steps here
    // would make those counts depend on test execution order. The seeded legacy ids (101, 102 for
    // profiles; 101 for species) are their own range, disjoint from the species (1-5) and profile
    // (10-20) fixtures the other step tests own. The seeding is idempotent (find or create) so it
    // does not care whether another test happened to create the same rows first.
    private Task RunPrerequisiteStepsAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var db = fixture.CreateTargetContext();

        if (!await db.Profiles.AnyAsync(p => p.LegacyId == 101))
        {
            db.Profiles.Add(new Profile
            {
                LegacyId = 101,
                Username = "tank-fixture-owner-101",
                DisplayName = "Tank Fixture Owner 101",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1000000000),
            });
        }

        if (!await db.Profiles.AnyAsync(p => p.LegacyId == 102))
        {
            db.Profiles.Add(new Profile
            {
                LegacyId = 102,
                Username = "tank-fixture-owner-102",
                DisplayName = "Tank Fixture Owner 102",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1000000000),
            });
        }

        if (!await db.Species.AnyAsync(s => s.LegacyId == 101))
        {
            db.Species.Add(new Species
            {
                LegacyId = 101,
                Genus = "Tankfixtura",
                Name = "testus",
                DisplayName = "Tankfixtura testus",
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1000000000),
            });
        }

        await db.SaveChangesAsync();
    });

    private Task<StepStatistics> RunStepAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, dryRun: false, CancellationToken.None);
        await EtlRunner.RunAsync(new TankMigrationStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("tanks");
    });
}
