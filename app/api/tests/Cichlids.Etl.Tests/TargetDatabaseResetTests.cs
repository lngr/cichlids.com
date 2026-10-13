// Story: task-3.30
using Cichlids.Domain.Entities;
using Cichlids.Etl.Runtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cichlids.Etl.Tests;

/// <summary>
/// The database part of the reset on databases of their own in the verify fixture's target
/// container.
/// </summary>
[Collection(VerifyEtlCollection.Name)]
public sealed class TargetDatabaseResetTests(VerifyEtlFixture fixture)
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task LeavesAnEmptyFullyMigratedDatabase(string environmentName)
    {
        var connectionString = await fixture.CreateEmptyTargetDatabaseAsync();
        await SeedSpeciesAsync(connectionString);
        await ExecuteAsync(connectionString, "CREATE TABLE public.stray (id int); INSERT INTO public.stray VALUES (1)");

        await TargetDatabaseReset.ResetAsync(connectionString, environmentName, CancellationToken.None);

        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.Species.CountAsync());
        Assert.True(await SchemaExistsAsync(connectionString, "archive"));
        Assert.False(await TableExistsAsync(connectionString, "public", "stray"));

        await using var transaction = await db.Database.BeginTransactionAsync();
        await EmptyTargetGuard.EnsureEmptyAsync(db, CancellationToken.None);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("")]
    public async Task RefusesOutsideDevelopmentAndStagingAndKeepsTheRows(string environmentName)
    {
        var connectionString = await fixture.CreateEmptyTargetDatabaseAsync();
        await SeedSpeciesAsync(connectionString);

        await Assert.ThrowsAsync<ResetRefusedException>(
            () => TargetDatabaseReset.ResetAsync(connectionString, environmentName, CancellationToken.None));

        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        Assert.Equal(1, await db.Species.CountAsync());
    }

    internal static async Task SeedSpeciesAsync(string connectionString)
    {
        await using var db = VerifyEtlFixture.CreateTargetContext(connectionString);
        db.Species.Add(new Species
        {
            Genus = "Tropheus",
            Name = "duboisi",
            DisplayName = "Tropheus duboisi",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    internal static async Task<bool> SchemaExistsAsync(string connectionString, string schema)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = @schema)", connection);
        command.Parameters.AddWithValue("schema", schema);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string schema, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table)", connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
