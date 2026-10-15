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
    private const int MaxMeasurementValues = 10;

    public static string? ValidateAmount(CareAmountRequest? amount)
    {
        if (amount is null) return null;
        if (amount.Unit is not ("liter" or "us_gallon" or "percent")) return "care_request_invalid";
        return amount.Value <= 0 || amount.Value > (amount.Unit == "percent" ? 100m : 1000000m)
            ? "care_amount_invalid" : null;
    }

    /// <summary>
    /// Validates the values of a measurement and builds them with their normalized values. Returns
    /// the problem code of the first refusal.
    /// </summary>
    public static (List<CareMeasurementValue>? Values, string? Error) BuildValues(IReadOnlyList<MeasurementValueRequest>? requests)
    {
        if (requests is null || requests.Count == 0 || requests.Count > MaxMeasurementValues) return (null, "care_measurement_invalid");
        var values = new List<CareMeasurementValue>();
        foreach (var request in requests)
        {
            if (!SnakeCaseEnum.TryParse<CareParameter>(request.Parameter, out var parameter)) return (null, "care_parameter_invalid");
            var rule = CareMeasurements.RuleFor(parameter);
            var unit = rule.InputUnits[0];
            if (request.Unit is not null && (!SnakeCaseEnum.TryParse<MeasurementUnit>(request.Unit, out unit) || !rule.InputUnits.Contains(unit)))
                return (null, "care_unit_invalid");
            MeasurementOrigin? origin = null;
            if (request.Origin is not null)
            {
                if (!SnakeCaseEnum.TryParse<MeasurementOrigin>(request.Origin, out var parsed)) return (null, "care_origin_invalid");
                origin = parsed;
            }
            var normalized = CareMeasurements.Normalize(parameter, request.Value, unit);
            if (!CareMeasurements.IsPlausible(parameter, normalized)) return (null, "care_value_invalid");
            if (values.Any(v => v.Parameter == parameter)) return (null, "care_measurement_invalid");
            values.Add(new CareMeasurementValue { Parameter = parameter, InputValue = request.Value, InputUnit = unit, NormalizedValue = normalized, Origin = origin });
        }
        return (values, null);
    }

    public async Task<(CareEntryDto? Entry, string? Error)> LogAsync(long tankId, long profileId, LogCareRequest request, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        if (!await LockOwnTankAsync(tankId, profileId, ct)) return (null, "care_not_found");
        if (!SnakeCaseEnum.TryParse<CareKind>(request.Kind, out var kind)) return (null, "care_kind_invalid");
        var now = AtMicrosecondPrecision(clock.GetUtcNow());
        if (request.OccurredAt > now.AddMinutes(2)) return (null, "care_time_invalid");
        var entry = new CareEntry
        {
            TankId = tankId, AuthorProfileId = profileId, Kind = kind,
            OccurredAt = AtMicrosecondPrecision((request.OccurredAt ?? now).ToUniversalTime()), RecordedAt = now,
        };
        if (kind == CareKind.Measurement)
        {
            if (request.Amount is not null) return (null, "care_request_invalid");
            var (values, valuesError) = BuildValues(request.Values);
            if (valuesError is not null) return (null, valuesError);
            entry.Values = values!;
        }
        else
        {
            if (request.Values is not null) return (null, "care_measurement_invalid");
            if (ValidateAmount(request.Amount) is { } error) return (null, error);
            SetAmount(entry, request.Amount);
        }
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
        var entry = await context.CareEntries.Include(e => e.Values)
            .SingleOrDefaultAsync(e => e.Id == id && e.TankId == tankId && e.AuthorProfileId == profileId, ct);
        if (entry is null) return (null, "care_not_found");
        if (request is not null)
        {
            if (entry.Kind == CareKind.Measurement)
            {
                if (request.Amount is not null) return (null, "care_request_invalid");
                var (values, valuesError) = BuildValues(request.Values);
                if (valuesError is not null) return (null, valuesError);
                ReplaceValues(entry, values!);
            }
            else
            {
                if (request.Values is not null) return (null, "care_measurement_invalid");
                if (ValidateAmount(request.Amount) is { } error) return (null, error);
                SetAmount(entry, request.Amount);
            }
        }
        var dto = CareEntryDto.From(entry);
        AddEvent(request is null ? "care.deleted" : "care.updated", entry, clock.GetUtcNow());
        if (request is null) context.CareEntries.Remove(entry);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (request is null ? dto : CareEntryDto.From(entry), null);
    }

    private async Task<bool> LockOwnTankAsync(long tankId, long profileId, CancellationToken ct)
    {
        var tank = await context.Tanks.FromSqlInterpolated($"SELECT * FROM public.tank WHERE id = {tankId} FOR UPDATE").SingleOrDefaultAsync(ct);
        return tank is not null && tank.ProfileId == profileId && tank.DeletedAt is null;
    }

    private static DateTimeOffset AtMicrosecondPrecision(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));

    private static void ReplaceValues(CareEntry entry, List<CareMeasurementValue> values)
    {
        foreach (var stale in entry.Values.Where(v => values.All(n => n.Parameter != v.Parameter)).ToList()) entry.Values.Remove(stale);
        foreach (var value in values)
        {
            var existing = entry.Values.SingleOrDefault(v => v.Parameter == value.Parameter);
            if (existing is null) entry.Values.Add(value);
            else
            {
                existing.InputValue = value.InputValue;
                existing.InputUnit = value.InputUnit;
                existing.NormalizedValue = value.NormalizedValue;
                existing.Origin = value.Origin;
            }
        }
    }

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

    private void AddEvent(string type, CareEntry entry, DateTimeOffset now)
    {
        var payload = new Dictionary<string, object?>
        {
            ["careEntryId"] = entry.Id, ["tankId"] = entry.TankId, ["authorProfileId"] = entry.AuthorProfileId,
            ["kind"] = SnakeCaseEnum.ToSnakeCase(entry.Kind), ["occurredAt"] = entry.OccurredAt, ["recordedAt"] = entry.RecordedAt,
            ["amount"] = entry.AmountInputValue is { } value ? new { value, unit = SnakeCaseEnum.ToSnakeCase(entry.AmountInputUnit!.Value), liters = entry.AmountLiters } : null,
        };
        if (entry.Kind == CareKind.Measurement)
            payload["values"] = entry.Values.OrderBy(v => v.Parameter).Select(v => new
            {
                parameter = SnakeCaseEnum.ToSnakeCase(v.Parameter), value = v.InputValue, unit = SnakeCaseEnum.ToSnakeCase(v.InputUnit),
                normalizedValue = v.NormalizedValue, canonicalUnit = SnakeCaseEnum.ToSnakeCase(CareMeasurements.RuleFor(v.Parameter).CanonicalUnit),
                origin = v.Origin is { } origin ? SnakeCaseEnum.ToSnakeCase(origin) : null,
            }).ToList();
        context.OutboxEvents.Add(OutboxEvent.Create(type, "care", entry.Id.ToString(), JsonSerializer.Serialize(payload), now));
    }
}
