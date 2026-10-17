using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Cichlids.Infrastructure.Tests.Persistence;

/// <summary>
/// The invariants of a stocking row hold in the database: a name implies a count of one, a removal day
/// and a removal reason come together, a removal does not precede the move-in, and a tank row cannot be
/// deleted while it has inhabitants. The migration gives existing rows the owner of their tank.
/// </summary>
[Trait("Category", "Docker")]
[Collection(PostgresCollection.Name)]
[Trait("Story", "TASK-9.1")]
public sealed class InhabitantConstraintTests(PostgresFixture fixture)
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    private async Task<(long Profile, long Tank)> TankAsync()
    {
        await using var context = fixture.CreateContext();
        var profile = await TestDataBuilder.CreateProfileAsync(context);
        return (profile.Id, (await TestDataBuilder.CreateTankAsync(context, profile.Id)).Id);
    }

    private async Task<string?> RefusalAsync(Inhabitant inhabitant)
    {
        await using var context = fixture.CreateContext();
        context.Inhabitants.Add(inhabitant);
        try
        {
            await context.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
        {
            return pg.ConstraintName;
        }
    }

    [Fact]
    public async Task A_group_a_named_fish_and_a_migrated_row_are_stored()
    {
        var (profile, tank) = await TankAsync();
        Assert.Null(await RefusalAsync(new Inhabitant { TankId = tank, OwnerProfileId = profile, Count = 12, MovedInOn = Day }));
        Assert.Null(await RefusalAsync(new Inhabitant { TankId = tank, OwnerProfileId = profile, Name = "Bruno", Count = 1, MovedInOn = Day }));
        Assert.Null(await RefusalAsync(new Inhabitant { TankId = tank, OwnerProfileId = profile, Count = 3 }));
        Assert.Null(await RefusalAsync(new Inhabitant
        {
            TankId = tank, OwnerProfileId = profile, Name = "Emma", Count = 1, MovedInOn = Day, RemovedOn = Day, RemovalReason = InhabitantRemovalReason.GivenAway,
        }));
    }

    [Fact]
    public async Task A_name_needs_a_count_of_one_and_may_not_be_blank()
    {
        var (profile, tank) = await TankAsync();
        Assert.Equal("ck_inhabitant_name_count", await RefusalAsync(new Inhabitant { TankId = tank, OwnerProfileId = profile, Name = "Bruno", Count = 2 }));
        Assert.Equal("ck_inhabitant_name_count", await RefusalAsync(new Inhabitant { TankId = tank, OwnerProfileId = profile, Name = "Bruno" }));
        Assert.Equal("ck_inhabitant_name_not_blank", await RefusalAsync(new Inhabitant { TankId = tank, OwnerProfileId = profile, Name = "  ", Count = 1 }));
    }

    [Fact]
    public async Task A_removal_day_and_a_reason_come_together_and_do_not_precede_the_move_in()
    {
        var (profile, tank) = await TankAsync();
        Assert.Equal("ck_inhabitant_removal", await RefusalAsync(new Inhabitant { TankId = tank, OwnerProfileId = profile, Count = 1, RemovedOn = Day }));
        Assert.Equal("ck_inhabitant_removal", await RefusalAsync(new Inhabitant { TankId = tank, OwnerProfileId = profile, Count = 1, RemovalReason = InhabitantRemovalReason.Died }));
        Assert.Equal("ck_inhabitant_dates", await RefusalAsync(new Inhabitant
        {
            TankId = tank, OwnerProfileId = profile, Count = 1, MovedInOn = Day, RemovedOn = Day.AddDays(-1), RemovalReason = InhabitantRemovalReason.Died,
        }));
    }

    [Fact]
    public async Task A_removal_reason_outside_the_known_values_is_refused()
    {
        var (profile, tank) = await TankAsync();
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO public.inhabitant (tank_id, owner_profile_id, count, sort) VALUES ({tank}, {profile}, 1, 0)");
        var ex = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.inhabitant SET removed_on = {Day}, removal_reason = 'vanished' WHERE tank_id = {tank}"));
        Assert.Equal("ck_inhabitant_removal_reason", ex.ConstraintName);
    }

    [Fact]
    public async Task A_tank_row_with_inhabitants_cannot_be_deleted()
    {
        var (profile, tank) = await TankAsync();
        Assert.Null(await RefusalAsync(new Inhabitant { TankId = tank, OwnerProfileId = profile, Name = "Bruno", Count = 1, MovedInOn = Day }));
        await using var context = fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM public.tank WHERE id = {tank}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }

    [Fact]
    public async Task The_migration_gives_existing_rows_the_owner_of_their_tank()
    {
        await using var database = await fixture.CreateEmptyDatabaseAsync();
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261017110126_ProfileReturnRetrospective");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO public.profile (id, username, kind, created_at) VALUES (1, 'stocking-owner', 'member', now());
            INSERT INTO public.tank (id, profile_id, title, state, created_at) VALUES (7, 1, 'Tank', 'published', now());
            INSERT INTO public.inhabitant (tank_id, count, sort) VALUES (7, 4, 0), (7, 2, 1);
            """);
        await migrator.MigrateAsync();
        var rows = await context.Inhabitants.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(1, r.OwnerProfileId);
            Assert.Null(r.MovedInOn);
            Assert.Null(r.Name);
            Assert.Null(r.RemovedOn);
        });
    }
}
