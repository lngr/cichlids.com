// Story: task-3.30
using System.Diagnostics;
using Cichlids.Domain.Entities;
using Cichlids.Etl.Runtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Etl.Tests;

/// <summary>
/// The guard of the full ETL run against databases of their own in the verify fixture's target
/// container: it passes on an empty, fully migrated database and names the reason when the
/// database holds a row, lacks a migration or is guarded by another run.
/// </summary>
[Collection(VerifyEtlCollection.Name)]
public sealed class EmptyTargetGuardTests(VerifyEtlFixture fixture)
{
    [Fact]
    public async Task PassesOnAnEmptyMigratedDatabase()
    {
        var connectionString = await fixture.CreateEmptyTargetDatabaseAsync();
        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();

        await EmptyTargetGuard.EnsureEmptyAsync(db, CancellationToken.None);
    }

    [Fact]
    public async Task AbortsOnADatabaseWithARowAndNamesItsTable()
    {
        var connectionString = await fixture.CreateEmptyTargetDatabaseAsync();
        await using (var seed = VerifyEtlFixture.CreateTargetContext(connectionString))
        {
            seed.Species.Add(new Species
            {
                Genus = "Aulonocara",
                Name = "jacobfreibergi",
                DisplayName = "Aulonocara jacobfreibergi",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var error = await Assert.ThrowsAsync<EmptyTargetException>(
            () => EmptyTargetGuard.EnsureEmptyAsync(db, CancellationToken.None));

        Assert.Equal(["public.species"], error.TablesWithRows);
        Assert.Contains("public.species", error.Message);
    }

    [Fact]
    public async Task AbortsOnADatabaseWithAPendingMigrationAndNamesIt()
    {
        string[] migrations;
        await using (var model = fixture.CreateTargetContext())
        {
            migrations = model.Database.GetMigrations().ToArray();
        }

        var connectionString = await fixture.CreateEmptyTargetDatabaseAsync(lastMigration: migrations[^2]);
        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var error = await Assert.ThrowsAsync<EmptyTargetException>(
            () => EmptyTargetGuard.EnsureEmptyAsync(db, CancellationToken.None));

        Assert.Contains(migrations[^1], error.Message);
    }

    [Fact]
    public async Task FailsFastWhileAnotherRunHoldsTheTargetLock()
    {
        var connectionString = await fixture.CreateEmptyTargetDatabaseAsync();

        await using var other = new NpgsqlConnection(connectionString);
        await other.OpenAsync();
        await using var otherTransaction = await other.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", other, otherTransaction))
        {
            lockCommand.Parameters.AddWithValue("key", EmptyTargetGuard.AdvisoryLockKey);
            await lockCommand.ExecuteNonQueryAsync();
        }

        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var stopwatch = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<EmptyTargetException>(
            () => EmptyTargetGuard.EnsureEmptyAsync(db, CancellationToken.None));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"the guard waited {stopwatch.Elapsed}");
        Assert.Contains("another ETL run", error.Message);
    }

    [Fact]
    public async Task RequiresAnOpenTransactionForItsLock()
    {
        var connectionString = await fixture.CreateEmptyTargetDatabaseAsync();
        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => EmptyTargetGuard.EnsureEmptyAsync(db, CancellationToken.None));
    }
}
