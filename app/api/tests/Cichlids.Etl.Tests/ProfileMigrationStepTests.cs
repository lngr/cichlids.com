using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Etl.Tests;

[Collection(EtlCollection.Name)]
public sealed class ProfileMigrationStepTests(EtlFixture fixture)
{
    [Fact]
    public async Task MigratesProfilesIdempotently()
    {
        var stats1 = await RunStepAsync();

        // Eligible: 10 (alice), 11 (bob), 13/14 (dup pair). 12 has no content, 15 is deleted,
        // 16 is disabled -- all three excluded by the source query itself.
        Assert.Equal(4, stats1.Read);
        Assert.Equal(4, stats1.Inserted);
        Assert.Equal(0, stats1.Updated);
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

            Assert.False(await db.Profiles.AnyAsync(p => p.LegacyId == 12));
            Assert.False(await db.Profiles.AnyAsync(p => p.LegacyId == 15));
            Assert.False(await db.Profiles.AnyAsync(p => p.LegacyId == 16));
        }

        var stats2 = await RunStepAsync();

        Assert.Equal(4, stats2.Read);
        Assert.Equal(0, stats2.Inserted);
        Assert.Equal(4, stats2.Updated);
        Assert.Equal(1, stats2.SkipReasons.GetValueOrDefault("profile_identity_duplicate"));

        await using (var db = fixture.CreateTargetContext())
        {
            // Scoped to this fixture's own legacy id range: TankMigrationStepTests seeds two
            // unrelated profile rows (legacy ids 101/102) and a placeholder (999) directly into
            // the same shared table.
            Assert.Equal(4, await db.Profiles.CountAsync(p => p.LegacyId != null && p.LegacyId <= 16));
            Assert.Equal(6, await db.ProfileIdentities.CountAsync());
        }
    }

    private Task<StepStatistics> RunStepAsync() => fixture.RunExclusiveAsync(async () =>
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, dryRun: false, CancellationToken.None);
        await EtlRunner.RunAsync(new ProfileMigrationStep(), context, CancellationToken.None);
        return context.Statistics.ForStep("profiles");
    });
}
