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
    [InlineData("kind = 'bogus'")]
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

    [Theory]
    [InlineData("log")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task GivenOutboxFailure_WhenWritingMeasurement_ThenEntryValuesAndEventRollBack(string operation)
    {
        await using var db = fixture.CreateDbContext();
        var tank = await db.Tanks.FirstAsync(t => t.DeletedAt == null);
        var entry = new CareEntry { TankId = tank.Id, AuthorProfileId = tank.ProfileId, Kind = CareKind.Measurement,
            OccurredAt = DateTimeOffset.UtcNow, RecordedAt = DateTimeOffset.UtcNow,
            Values = [new CareMeasurementValue { Parameter = CareParameter.Ph, InputValue = 7, InputUnit = MeasurementUnit.None, NormalizedValue = 7 }] };
        db.CareEntries.Add(entry);
        await db.SaveChangesAsync();
        var entries = await db.CareEntries.CountAsync();
        var values = await db.CareMeasurementValues.CountAsync();
        var eventsBefore = await db.OutboxEvents.CountAsync();
        var ph = new MeasurementValueRequest("ph", 8);
        await using (var write = fixture.CreateDbContext(new RefuseOutbox()))
        {
            var service = new CareWriteService(write, TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                if (operation == "log") await service.LogAsync(tank.Id, tank.ProfileId, new(Kind: "measurement", Values: [ph]), CancellationToken.None);
                else await service.ChangeAsync(tank.Id, entry.Id, tank.ProfileId,
                    operation == "delete" ? null : new(null, [new MeasurementValueRequest("no3", 5)]), CancellationToken.None);
            });
        }
        Assert.Equal(entries, await db.CareEntries.CountAsync());
        Assert.Equal(values, await db.CareMeasurementValues.CountAsync());
        Assert.Equal(eventsBefore, await db.OutboxEvents.CountAsync());
        var stored = await db.CareMeasurementValues.AsNoTracking().SingleAsync(v => v.CareEntryId == entry.Id);
        Assert.Equal((CareParameter.Ph, 7m), (stored.Parameter, stored.NormalizedValue));
        db.CareEntries.Remove(entry);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("parameter = 'salinity'")]
    [InlineData("origin = 'guess'")]
    [InlineData("normalized_value = 14.5")]
    [InlineData("input_unit = 'celsius'")]
    public async Task GivenInvalidPersistedValue_WhenSaved_ThenDatabaseConstraintRefuses(string assignment)
    {
        await using var db = fixture.CreateDbContext();
        var tank = await db.Tanks.FirstAsync(t => t.DeletedAt == null);
        var service = new CareWriteService(db, TimeProvider.System);
        var (entry, error) = await service.LogAsync(tank.Id, tank.ProfileId, new(Kind: "measurement", Values: [new("ph", 7)]), CancellationToken.None);
        Assert.Null(error);
        #pragma warning disable EF1002 // Assignments are fixed test cases; the entry ID is parameterized.
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            $"UPDATE public.care_measurement_value SET {assignment} WHERE care_entry_id = {{0}}", entry!.Id));
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
