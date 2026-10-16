using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Tanks.TankVisibilityTestSupport;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// One owner with three tanks at the level pairs public/public, public/private and private/private,
/// each with a public water change, a public ph measurement, a water change marked only me and a
/// ph measurement marked only me, plus a second member who owns none of them.
/// </summary>
internal sealed class TankVisibilityWorld
{
    public required Member Owner { get; init; }
    public required Member Other { get; init; }
    public required long OpenTank { get; init; }
    public required long StreamPrivateTank { get; init; }
    public required long PrivateTank { get; init; }
    public required IReadOnlyDictionary<long, TankEntries> Entries { get; init; }

    public sealed record TankEntries(long PublicWaterChange, long PublicMeasurement, long OnlyMeWaterChange, long OnlyMeMeasurement, long Todo);

    public IReadOnlyList<long> AllTanks => [OpenTank, StreamPrivateTank, PrivateTank];

    public static async Task<TankVisibilityWorld> CreateAsync(ApiFixture fixture)
    {
        var owner = await NewMemberAsync(fixture);
        var other = await NewMemberAsync(fixture);
        var open = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Public);
        var streamPrivate = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Public, TankVisibility.Private);
        var priv = await NewTankAsync(fixture, owner.ProfileId, TankVisibility.Private, TankVisibility.Private);
        var entries = new Dictionary<long, TankEntries>();
        await using var db = fixture.CreateDbContext();
        foreach (var tank in new[] { open, streamPrivate, priv })
        {
            var publicWater = Entry(tank, owner.ProfileId, CareKind.WaterChange, daysAgo: 10, onlyMe: false);
            var publicPh = Entry(tank, owner.ProfileId, CareKind.Measurement, daysAgo: 9, onlyMe: false, ph: 7.0m);
            var secretWater = Entry(tank, owner.ProfileId, CareKind.WaterChange, daysAgo: 1, onlyMe: true);
            var secretPh = Entry(tank, owner.ProfileId, CareKind.Measurement, daysAgo: 0, onlyMe: true, ph: 8.5m);
            db.CareEntries.AddRange(publicWater, publicPh, secretWater, secretPh);
            await db.SaveChangesAsync();
            var todo = new CareTodo
            {
                ProfileId = owner.ProfileId, TankId = tank, CareEntryId = publicWater.Id, Text = "Clean the filter", CreatedAt = DateTimeOffset.UtcNow,
            };
            db.CareTodos.Add(todo);
            await db.SaveChangesAsync();
            entries[tank] = new TankEntries(publicWater.Id, publicPh.Id, secretWater.Id, secretPh.Id, todo.Id);
        }

        return new TankVisibilityWorld
        {
            Owner = owner, Other = other, OpenTank = open, StreamPrivateTank = streamPrivate, PrivateTank = priv, Entries = entries,
        };
    }

    public async Task DisposeAsync(ApiFixture fixture)
    {
        await using var db = fixture.CreateDbContext();
        await db.Ratings.Where(r => r.TankId != null && AllTanks.Contains(r.TankId.Value)).ExecuteDeleteAsync();
        await db.Comments.Where(c => c.TankId != null && AllTanks.Contains(c.TankId.Value)).ExecuteDeleteAsync();
        await db.CareTodos.Where(t => AllTanks.Contains(t.TankId)).ExecuteDeleteAsync();
        await RemoveTanksAsync(fixture, AllTanks.ToArray());
    }

    private static CareEntry Entry(long tank, long author, CareKind kind, int daysAgo, bool onlyMe, decimal? ph = null)
    {
        var occurred = DateTimeOffset.UtcNow.AddDays(-daysAgo);
        var entry = new CareEntry
        {
            TankId = tank, AuthorProfileId = author, Kind = kind, OccurredAt = occurred, RecordedAt = occurred, OnlyMe = onlyMe,
            AmountInputValue = kind == CareKind.WaterChange ? 10m : null,
            AmountInputUnit = kind == CareKind.WaterChange ? CareAmountUnit.Liter : null,
            AmountLiters = kind == CareKind.WaterChange ? 10m : null,
        };
        if (ph is { } value)
        {
            entry.Values.Add(new CareMeasurementValue
            {
                Parameter = CareParameter.Ph, InputValue = value, InputUnit = MeasurementUnit.None, NormalizedValue = value,
            });
        }

        return entry;
    }
}
