using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Care;

public sealed class CareWriteService(CichlidsDbContext context, TimeProvider clock)
{
    public static string? ValidateAmount(CareAmountRequest? amount)
    {
        if (amount is null) return null;
        if (amount.Unit is not ("liter" or "us_gallon" or "percent")) return "care_request_invalid";
        return amount.Value <= 0 || amount.Value > (amount.Unit == "percent" ? 100m : 1000000m)
            ? "care_amount_invalid" : null;
    }

    public async Task<(CareEntryDto? Entry, string? Error)> LogAsync(long tankId, long profileId, LogCareRequest request, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await LockOwnTankAsync(tankId, profileId, ct)) return (null, "care_not_found");
        if (request.Kind != "water_change") return (null, "care_kind_invalid");
        var now = AtMicrosecondPrecision(clock.GetUtcNow());
        if (request.OccurredAt > now.AddMinutes(2)) return (null, "care_time_invalid");
        if (ValidateAmount(request.Amount) is { } error) return (null, error);
        var entry = new CareEntry
        {
            TankId = tankId, AuthorProfileId = profileId, Kind = CareKind.WaterChange,
            OccurredAt = AtMicrosecondPrecision((request.OccurredAt ?? now).ToUniversalTime()), RecordedAt = now,
        };
        SetAmount(entry, request.Amount);
        context.CareEntries.Add(entry);
        await context.SaveChangesAsync(ct);
        AddEvent("care.logged", entry, now);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (CareEntryDto.From(entry), null);
    }

    public async Task<(CareEntryDto? Entry, string? Error)> ChangeAsync(long tankId, long id, long profileId,
        UpdateCareRequest? request, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await LockOwnTankAsync(tankId, profileId, ct)) return (null, "care_not_found");
        var entry = await context.CareEntries.SingleOrDefaultAsync(e => e.Id == id && e.TankId == tankId && e.AuthorProfileId == profileId, ct);
        if (entry is null) return (null, "care_not_found");
        if (request is not null)
        {
            if (ValidateAmount(request.Amount) is { } error) return (null, error);
            SetAmount(entry, request.Amount);
        }
        else context.CareEntries.Remove(entry);
        AddEvent(request is null ? "care.deleted" : "care.updated", entry, clock.GetUtcNow());
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (CareEntryDto.From(entry), null);
    }

    private async Task<bool> LockOwnTankAsync(long tankId, long profileId, CancellationToken ct)
    {
        var tank = await context.Tanks.FromSqlInterpolated($"SELECT * FROM public.tank WHERE id = {tankId} FOR UPDATE").SingleOrDefaultAsync(ct);
        return tank is not null && tank.ProfileId == profileId && tank.DeletedAt is null;
    }

    private static DateTimeOffset AtMicrosecondPrecision(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));

    private static void SetAmount(CareEntry entry, CareAmountRequest? amount)
    {
        entry.AmountInputValue = amount?.Value;
        entry.AmountInputUnit = amount?.Unit switch
        {
            "liter" => CareAmountUnit.Liter, "us_gallon" => CareAmountUnit.UsGallon,
            "percent" => CareAmountUnit.Percent, _ => null,
        };
        entry.AmountLiters = amount?.Unit switch
        {
            "liter" => amount.Value, "us_gallon" => amount.Value * TankMeasurements.LitersPerUsGallon, _ => null,
        };
    }

    private void AddEvent(string type, CareEntry entry, DateTimeOffset now) => context.OutboxEvents.Add(
        OutboxEvent.Create(type, "care", entry.Id.ToString(), JsonSerializer.Serialize(new
        {
            careEntryId = entry.Id, tankId = entry.TankId, authorProfileId = entry.AuthorProfileId,
            kind = SnakeCaseEnum.ToSnakeCase(entry.Kind), occurredAt = entry.OccurredAt, recordedAt = entry.RecordedAt,
            amount = entry.AmountInputValue is { } value ? new { value, unit = SnakeCaseEnum.ToSnakeCase(entry.AmountInputUnit!.Value), liters = entry.AmountLiters } : null,
        }), now));
}
