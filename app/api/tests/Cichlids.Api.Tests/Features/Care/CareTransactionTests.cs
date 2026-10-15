using Cichlids.Api.Features.Care;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(ApiCollection.Name)]
public sealed class CareTransactionTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("log")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task GivenOutboxFailure_WhenWritingCare_ThenEntryAndEventRollBack(string operation)
    {
        await using var db = fixture.CreateDbContext();
        var tank = await db.Tanks.FirstAsync(t => t.DeletedAt == null);
        var entry = new CareEntry { TankId = tank.Id, AuthorProfileId = tank.ProfileId,
            Kind = CareKind.WaterChange, OccurredAt = DateTimeOffset.UtcNow, RecordedAt = DateTimeOffset.UtcNow };
        db.CareEntries.Add(entry);
        await db.SaveChangesAsync();
        var before = await db.CareEntries.CountAsync();
        var eventsBefore = await db.OutboxEvents.CountAsync();
        await using (var write = fixture.CreateDbContext(new RefuseOutbox()))
        {
            var service = new CareWriteService(write, TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                if (operation == "log") await service.LogAsync(tank.Id, tank.ProfileId, new(), CancellationToken.None);
                else await service.ChangeAsync(tank.Id, entry.Id, tank.ProfileId,
                    operation == "delete" ? null : new(new(20, "liter")), CancellationToken.None);
            });
        }
        Assert.Equal(before, await db.CareEntries.CountAsync());
        Assert.Equal(eventsBefore, await db.OutboxEvents.CountAsync());
        await db.Entry(entry).ReloadAsync();
        Assert.Null(entry.AmountInputValue);
        db.CareEntries.Remove(entry);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("kind = 'measurement'")]
    [InlineData("amount_input_value = 20")]
    [InlineData("amount_input_value = 20, amount_input_unit = 'liter'")]
    [InlineData("amount_input_value = 20, amount_input_unit = 'liter', amount_liters = 21")]
    [InlineData("amount_input_value = 101, amount_input_unit = 'percent'")]
    [InlineData("amount_input_value = 20, amount_input_unit = 'percent', amount_liters = 20")]
    [InlineData("occurred_at = recorded_at + interval '3 minutes'")]
    public async Task GivenInvalidPersistedShape_WhenSaved_ThenDatabaseConstraintRefuses(string assignment)
    {
        await using var db = fixture.CreateDbContext();
        var tank = await db.Tanks.FirstAsync(t => t.DeletedAt == null);
        var service = new CareWriteService(db, TimeProvider.System);
        var (entry, error) = await service.LogAsync(tank.Id, tank.ProfileId, new(), CancellationToken.None);
        Assert.Null(error);
        #pragma warning disable EF1002 // Assignments are fixed test cases; the entry ID is parameterized.
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            $"UPDATE public.care_entry SET {assignment} WHERE id = {{0}}", entry!.Id));
        #pragma warning restore EF1002
        await db.OutboxEvents.Where(e => e.AggregateType == "care" && e.AggregateId == entry!.Id.ToString()).ExecuteDeleteAsync();
        await db.CareEntries.Where(e => e.Id == entry!.Id).ExecuteDeleteAsync();
    }

    private sealed class RefuseOutbox : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<OutboxEvent>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Test refuses outbox write.");
            return ValueTask.FromResult(result);
        }
    }
}
