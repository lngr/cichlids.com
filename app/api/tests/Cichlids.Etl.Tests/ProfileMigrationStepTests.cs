using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Cichlids.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(EtlCollection.Name)]
public sealed class ProfileMigrationStepTests(EtlFixture fixture)
{
    [Fact]
    public async Task MigratesProfilesIdempotently()
    {
        var names = new GeneratedNames(EtlContext.DevDefaultSlugSecret);
        var eveHandle = names.Generate(GeneratedNames.HandleInput("17"));
        var ginaCandidates = names.Candidates(GeneratedNames.HandleInput("19")).Take(2).ToList();
        var hankHandle = names.Generate(GeneratedNames.HandleInput("20"));
        await SeedTargetProfilesAsync(ginaCandidates[0]);

        var stats1 = await RunStepAsync();

        // Eligible: 10 (alice), 11 (bob), 13/14 (dup pair), 17 to 20. 12 has no content, 15 is
        // deleted, 16 is disabled -- all three excluded by the source query itself. 20 exists in
        // the target with an email address as its handle and display name.
        Assert.Equal(8, stats1.Read);
        Assert.Equal(7, stats1.Inserted);
        Assert.Equal(1, stats1.Updated);
        Assert.Equal(1, stats1.SkipReasons.GetValueOrDefault("profile_identity_duplicate"));
        var usernameWarning = Assert.Single(stats1.Warnings);
        Assert.Contains("dupuser", usernameWarning);

        await using (var db = fixture.CreateTargetContext())
        {
            var alice = await db.Profiles.Include(p => p.Identities).SingleAsync(p => p.LegacyId == 10);
            Assert.Equal("alice", alice.Username);
            Assert.Equal("Alice A", alice.DisplayName);
            Assert.Equal("Berlin", alice.City);
            Assert.Equal("DEU", alice.CountryCode);
            Assert.Equal("http://img/alice.png", alice.ExternalAvatarUrl);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000000000), alice.CreatedAt);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000000200), alice.LastLoginAt);
            Assert.Equal(3, alice.Identities.Count);
            Assert.Contains(alice.Identities, i => i.Provider == "auth0" && i.Subject == "auth0|alice");
            Assert.Contains(alice.Identities, i => i.Provider == "legacy-openid" && i.Subject == "openid-alice");
            Assert.Contains(alice.Identities, i => i.Provider == "email" && i.Subject == "alice@example.com");

            var bob = await db.Profiles.Include(p => p.Identities).SingleAsync(p => p.LegacyId == 11);
            Assert.Equal("Bob B", bob.DisplayName);
            Assert.Equal(new DateTimeOffset(2003, 1, 1, 0, 0, 0, TimeSpan.Zero), bob.CreatedAt);
            Assert.Null(bob.LastLoginAt);
            Assert.Empty(bob.Identities);

            var dup1 = await db.Profiles.Include(p => p.Identities).SingleAsync(p => p.LegacyId == 13);
            var dup2 = await db.Profiles.Include(p => p.Identities).SingleAsync(p => p.LegacyId == 14);
            Assert.Equal("dupuser", dup1.Username);
            Assert.Equal("dupuser-14", dup2.Username);
            Assert.Equal(2, dup1.Identities.Count);
            Assert.Contains(dup1.Identities, i => i.Provider == "legacy-openid" && i.Subject == "dup-openid");
            var dup2Identity = Assert.Single(dup2.Identities);
            Assert.Equal("auth0", dup2Identity.Provider);
            Assert.Equal("auth0|dup14", dup2Identity.Subject);

            var eve = await db.Profiles.SingleAsync(p => p.LegacyId == 17);
            Assert.Equal(eveHandle, eve.Username);
            Assert.Equal(eveHandle, eve.DisplayName);
            Assert.Null(eve.ExternalAvatarUrl);

            var frank = await db.Profiles.SingleAsync(p => p.LegacyId == 18);
            Assert.Equal("frank", frank.Username);
            Assert.Equal("Frank F", frank.DisplayName);
            Assert.Null(frank.ExternalAvatarUrl);

            // The first candidate of gina's handle belongs to a profile the API created.
            var gina = await db.Profiles.SingleAsync(p => p.LegacyId == 19);
            Assert.Equal(ginaCandidates[1], gina.Username);
            Assert.Equal("Gina G", gina.DisplayName);

            // hank's pre-existing row held a Gravatar URL from before this rule existed; the run
            // clears it because the legacy image column is empty, proving the upsert overwrites a
            // stale stored Gravatar URL rather than leaving it in place.
            var hank = await db.Profiles.SingleAsync(p => p.LegacyId == 20);
            Assert.Equal(hankHandle, hank.Username);
            Assert.Equal(hankHandle, hank.DisplayName);
            Assert.Null(hank.ExternalAvatarUrl);

            Assert.False(await db.Profiles.AnyAsync(p => p.LegacyId == 12));
            Assert.False(await db.Profiles.AnyAsync(p => p.LegacyId == 15));
            Assert.False(await db.Profiles.AnyAsync(p => p.LegacyId == 16));
        }

        var stats2 = await RunStepAsync();

        Assert.Equal(8, stats2.Read);
        Assert.Equal(0, stats2.Inserted);
        Assert.Equal(8, stats2.Updated);
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("profile_identity_duplicate"));

        await using (var db = fixture.CreateTargetContext())
        {
            // Scoped to this fixture's own legacy id range: TankMigrationStepTests seeds two
            // unrelated profile rows (legacy ids 101/102) and a placeholder (999) directly into
            // the same shared table.
            Assert.Equal(8, await db.Profiles.CountAsync(p => p.LegacyId != null && p.LegacyId <= 20));
            Assert.Equal(6, await db.ProfileIdentities.CountAsync());

            var handles = await db.Profiles.Where(p => p.LegacyId >= 17 && p.LegacyId <= 20).OrderBy(p => p.LegacyId).Select(p => p.Username).ToListAsync();
            Assert.Equal([eveHandle, "frank", ginaCandidates[1], hankHandle], handles);
        }
    }

    // A member profile the API created on a first login holds the first candidate of gina's
    // handle, and hank's profile exists from a run that took his email address as handle and
    // display name.
    private Task SeedTargetProfilesAsync(string takenHandle) => fixture.RunExclusiveAsync(async () =>
    {
        await using var db = fixture.CreateTargetContext();
        if (!await db.Profiles.AnyAsync(p => p.Username == takenHandle))
        {
            db.Profiles.Add(new Profile { Username = takenHandle, DisplayName = takenHandle, Kind = ProfileKind.Member, CreatedAt = DateTimeOffset.UtcNow });
        }

        if (!await db.Profiles.AnyAsync(p => p.LegacyId == 20))
        {
            db.Profiles.Add(new Profile
            {
                LegacyId = 20,
                Username = "hank@example.com",
                DisplayName = "hank@example.com",
                ExternalAvatarUrl = "https://s.gravatar.com/avatar/stale?s=1",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(1000000000),
            });
        }

        await db.SaveChangesAsync();
    });

    private Task<StepStatistics> RunStepAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, dryRun: false, CancellationToken.None);
        await EtlRunner.RunAsync(new ProfileMigrationStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("profiles");
    });
}
