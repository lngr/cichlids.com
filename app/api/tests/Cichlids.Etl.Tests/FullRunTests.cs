// Story: task-3.30
using Cichlids.Domain.Entities;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Etl.Tests;

/// <summary>
/// The full ETL run on databases of their own in the verify fixture's target container: its write
/// steps share one transaction, so a failing step leaves the target empty, and its verification
/// runs after the commit, so a mismatch fails the run and keeps the written rows.
/// </summary>
[Collection(VerifyEtlCollection.Name)]
public sealed class FullRunTests(VerifyEtlFixture fixture)
{
    [Fact]
    public async Task LeavesEveryTableEmptyWhenAStepFails()
    {
        var targetConnectionString = await fixture.CreateEmptyTargetDatabaseAsync();
        await using (var context = await EtlContext.CreateAsync(
                         fixture.LegacyConnectionString, targetConnectionString, CancellationToken.None, fixture.ObjectStore))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => EtlRunner.RunAllAsync(
                context,
                [new SpeciesCatalogStep(), new FailingStep()],
                EtlStepRegistry.Verify,
                afterGuard: null,
                CancellationToken.None));
        }

        await using var db = VerifyEtlFixture.CreateTargetContext(targetConnectionString);
        Assert.Equal(0, await db.Species.CountAsync());
        await using var transaction = await db.Database.BeginTransactionAsync();
        await EmptyTargetGuard.EnsureEmptyAsync(db, CancellationToken.None);
    }

    [Fact]
    public async Task WritesNothingWhenTheTargetHoldsARow()
    {
        var targetConnectionString = await fixture.CreateEmptyTargetDatabaseAsync();
        await using (var seed = VerifyEtlFixture.CreateTargetContext(targetConnectionString))
        {
            seed.Species.Add(new Species
            {
                Genus = "Aulonocara",
                Name = "stuartgranti",
                DisplayName = "Aulonocara stuartgranti",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var afterGuardCalled = false;
        await using (var context = await EtlContext.CreateAsync(
                         fixture.LegacyConnectionString, targetConnectionString, CancellationToken.None, fixture.ObjectStore))
        {
            var error = await Assert.ThrowsAsync<EmptyTargetException>(() => EtlRunner.RunAllAsync(
                context,
                EtlStepRegistry.WriteSteps,
                EtlStepRegistry.Verify,
                _ =>
                {
                    afterGuardCalled = true;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

            Assert.Equal(["public.species"], error.TablesWithRows);
        }

        Assert.False(afterGuardCalled);
        await using var db = VerifyEtlFixture.CreateTargetContext(targetConnectionString);
        Assert.Equal(1, await db.Species.CountAsync());
        Assert.Equal(0, await db.Profiles.CountAsync());
    }

    [Fact]
    public async Task FailsOnAMismatchAndKeepsTheCommittedRows()
    {
        var targetConnectionString = await fixture.CreateEmptyTargetDatabaseAsync();
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, targetConnectionString, CancellationToken.None, fixture.ObjectStore);

        var exitCode = await EtlRunner.RunAllAsync(
            context,
            [.. EtlStepRegistry.WriteSteps, new RogueMemberStep()],
            EtlStepRegistry.Verify,
            afterGuard: null,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        var mismatch = Assert.Single(context.VerificationReport!.Rows, row => row.Status == VerificationStatus.Mismatch);
        Assert.Equal("profiles_member", mismatch.Entity);

        await using var db = VerifyEtlFixture.CreateTargetContext(targetConnectionString);
        Assert.Equal(2, await db.Species.CountAsync());
        Assert.Equal(1, await db.Profiles.CountAsync(profile => profile.LegacyId == RogueMemberStep.LegacyId));
    }

    private sealed class FailingStep : IEtlStep
    {
        public string Name => "failing";

        public int Order => 1;

        public Task RunAsync(EtlContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The step fails on purpose.");
    }

    /// <summary>
    /// Writes a member profile with a legacy id no legacy row justifies, so verification reports a
    /// mismatch for the migrated member count.
    /// </summary>
    private sealed class RogueMemberStep : IEtlStep
    {
        public const int LegacyId = 999_002;

        public string Name => "rogue-member";

        public int Order => 79;

        public async Task RunAsync(EtlContext context, CancellationToken cancellationToken)
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO profile (legacy_id, username, display_name, kind, created_at) "
                + "VALUES (@legacyId, 'full-run-rogue-member', 'Rogue Member', 'member', now())",
                context.Target,
                context.Transaction);
            insert.Parameters.AddWithValue("legacyId", LegacyId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
